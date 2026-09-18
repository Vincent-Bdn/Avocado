using Avocado.Server.Data;
using Avocado.Server.Features.Templates.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Templates.Endpoints;

public static class TemplateEndpoints
{
    /// <summary>
    /// One modèle, for one thing: the détail that goes out with a facture.
    ///
    /// <para><b>This used to be a general system</b>, with a free-text « kind » on each template and
    /// a « Générer depuis un modèle » endpoint for filling a lettre de mission. Two years of that
    /// existed and nobody used it, for a reason that only became obvious when somebody tried: the
    /// generation had no button anywhere in the window, and the kind, which is what decides whether a
    /// template is ever used, could not be set while uploading. It had to be typed afterwards, into a
    /// rename field, spelled exactly « Détail de facture ». A template uploaded the obvious way was
    /// inert, and the screen gave no hint why.</para>
    ///
    /// <para>So there is no kind to get wrong, no catalogue to choose between and nothing to
    /// generate. She drops the document she already sends, and it is used.</para>
    /// </summary>
    public static IEndpointRouteBuilder MapTemplates(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/templates").WithTags("Templates");

        group.MapGet("/", ListTemplates.HandleAsync);
        group.MapGet("/fields", () => Results.Ok(
            Billings.Infrastructure.BillingDetailTemplate.Catalogue
                .Select(entry => new { field = entry.Field, example = entry.Description })));
        group.MapPost("/", UploadTemplate.HandleAsync).DisableAntiforgery();
        group.MapDelete("/{id:guid}", DeleteTemplate.HandleAsync);
        group.MapGet("/{id:guid}/content", DownloadTemplate.HandleAsync);

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

        // Set here rather than asked for. It is the only kind there is, and having to type it was
        // exactly what made an uploaded template do nothing.
        var template = new DocumentTemplate
        {
            Name = Trimmed(form["name"].ToString()) ?? Path.GetFileNameWithoutExtension(file.FileName),
            Kind = Billings.Infrastructure.BillingDetailTemplate.Kind,
            FileName = file.FileName,
            BlobSha256 = blob.Sha256,
            SizeBytes = blob.SizeBytes,
        };

        // One at a time: a second would leave « which of these is used » answerable only by reading
        // the code. Uploading replaces.
        database.Templates.RemoveRange(await database.Templates.ToListAsync(cancellationToken));
        database.Templates.Add(template);

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/templates/{template.Id}", new { template.Id });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        // Only other modèles can share this blob now: generated letters are written into her folder
        // rather than stored here.
        var stillReferenced = await database.Templates
            .AnyAsync(other => other.Id != template.Id && other.BlobSha256 == template.BlobSha256, cancellationToken);

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
