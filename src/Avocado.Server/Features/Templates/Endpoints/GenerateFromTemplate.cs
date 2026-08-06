using Avocado.Server.Data;
using Avocado.Server.Features.Documents;
using Avocado.Server.Features.Documents.Endpoints;
using Avocado.Server.Features.Templates.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Templates.Endpoints;

public sealed record GenerateInput(string? FileName, string? Folder);

/// <summary>
/// Fills a modèle with this dossier's own wording and files the result as a document.
/// <para>
/// It lands in the coffre rather than in a download, because the point is that she then opens it,
/// finishes the sentences Word cannot write for her, and the edits go straight back — a generated
/// letter is a draft, not an export.
/// </para>
/// </summary>
public static class GenerateFromTemplate
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        Guid templateId,
        GenerateInput input,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var template = await database.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);

        if (template is null)
        {
            return Results.NotFound();
        }

        var vault = vaultStore.Get(tenant.VaultId);
        var client = matter.Parties.FirstOrDefault(party => party.IsClient)?.Contact;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        byte[] filled;
        await using (var source = vault.Blobs.OpenRead(new BlobReference(template.BlobSha256, template.SizeBytes)))
        {
            filled = TemplateMerge.Fill(source, TemplateFields.For(matter, client, today));
        }

        BlobReference blob;
        using (var content = new MemoryStream(filled))
        {
            blob = await vault.Blobs.PutAsync(content, cancellationToken);
        }

        var name = string.IsNullOrWhiteSpace(input.FileName)
            ? $"{Slug(template.Name)}-{matter.Reference}.docx"
            : EnsureDocx(input.FileName.Trim());

        var document = new Document
        {
            MatterId = matterId,
            BlobSha256 = blob.Sha256,
            SizeBytes = blob.SizeBytes,
            FileName = name,
            Folder = DocumentFolder.Normalise(input.Folder),
            Type = template.Kind,
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            DocumentDate = today,
        };

        database.Documents.Add(document);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/documents/{document.Id}", new { document.Id, document.FileName });
    }

    private static string EnsureDocx(string name) =>
        name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.docx";

    /// <summary>A file name, not a URL slug: accents stay, only what a filesystem refuses is replaced.</summary>
    private static string Slug(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();

        return new string([.. name.Select(character => invalid.Contains(character) ? '-' : character)])
            .Replace(' ', '-')
            .ToLowerInvariant();
    }
}
