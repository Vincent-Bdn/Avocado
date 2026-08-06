using Avocado.Server.Data;
using Avocado.Server.Features.Templates.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Templates.Endpoints;

public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/templates").WithTags("Templates");

        group.MapGet("/", ListTemplates.HandleAsync);
        group.MapGet("/fields", () => Results.Ok(
            TemplateFields.Catalogue.Select(entry => new { field = entry.Field, example = entry.Description })));
        group.MapPost("/", UploadTemplate.HandleAsync).DisableAntiforgery();
        group.MapPut("/{id:guid}", RenameTemplate.HandleAsync);
        group.MapDelete("/{id:guid}", DeleteTemplate.HandleAsync);
        group.MapGet("/{id:guid}/content", DownloadTemplate.HandleAsync);

        routes.MapPost("/api/matters/{matterId:guid}/documents/from-template/{templateId:guid}",
            GenerateFromTemplate.HandleAsync).WithTags("Templates");

        return routes;
    }
}

public static class ListTemplates
{
    public static async Task<IResult> HandleAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var templates = await database.Templates
            .AsNoTracking()
            .OrderBy(template => template.Kind)
            .ThenBy(template => template.Name)
            .Select(template => new
            {
                template.Id,
                template.Name,
                template.Kind,
                template.FileName,
                template.SizeBytes,
                template.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(templates);
    }
}

public static class UploadTemplate
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.FirstOrDefault();

        if (file is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["Choisissez un fichier .docx."],
            });
        }

        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                // .doc is the old binary format, which OpenXml cannot read at all. Saying so beats a
                // stack trace about a corrupt zip.
                ["file"] = ["Un modèle doit être un fichier .docx, enregistré depuis Word."],
            });
        }

        var vault = vaultStore.Get(tenant.VaultId);

        BlobReference blob;
        await using (var content = file.OpenReadStream())
        {
            blob = await vault.Blobs.PutAsync(content, cancellationToken);
        }

        var template = new DocumentTemplate
        {
            Name = Trimmed(form["name"].ToString()) ?? Path.GetFileNameWithoutExtension(file.FileName),
            Kind = Trimmed(form["kind"].ToString()),
            FileName = file.FileName,
            BlobSha256 = blob.Sha256,
            SizeBytes = blob.SizeBytes,
        };

        database.Templates.Add(template);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/templates/{template.Id}", new { template.Id });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TemplateInput(string Name, string? Kind);

public static class RenameTemplate
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        TemplateInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Donnez un nom au modèle."],
            });
        }

        var template = await database.Templates
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return Results.NotFound();
        }

        template.Name = input.Name.Trim();
        template.Kind = string.IsNullOrWhiteSpace(input.Kind) ? null : input.Kind.Trim();
        template.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}

public static class DeleteTemplate
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var template = await database.Templates
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return Results.NoContent();
        }

        database.Templates.Remove(template);
        await database.SaveChangesAsync(cancellationToken);

        var reference = new BlobReference(template.BlobSha256, template.SizeBytes);

        var stillReferenced =
            await database.Templates.AnyAsync(t => t.BlobSha256 == template.BlobSha256, cancellationToken) ||
            await database.Documents.AnyAsync(d => d.BlobSha256 == template.BlobSha256, cancellationToken);

        if (!stillReferenced)
        {
            vaultStore.Get(tenant.VaultId).Blobs.Delete(reference);
        }

        return Results.NoContent();
    }
}

public static class DownloadTemplate
{
    public static async Task<Results<FileStreamHttpResult, NotFound>> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var template = await database.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (template is null)
        {
            return TypedResults.NotFound();
        }

        var stream = vaultStore.Get(tenant.VaultId).Blobs
            .OpenRead(new BlobReference(template.BlobSha256, template.SizeBytes));

        return TypedResults.Stream(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            template.FileName);
    }
}
