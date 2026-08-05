using Avocado.Server.Data;
using Avocado.Vault;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <summary>
/// Streams an uploaded file into the encrypted blob store, then records it.
/// <para>
/// A drop always creates plain documents, never pièces — « ils arrivent comme documents. Vous leur
/// donnerez un n° de pièce si besoin. » Numbering evidence is a legal act and is never a side effect
/// of dragging a file.
/// </para>
/// </summary>
public static class UploadDocument
{
    /// <summary>Matches the drop zone's stated limit of 50 Mo per file.</summary>
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    public static async Task<IResult> HandleAsync(
        Guid matterId,
        IFormFileCollection files,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        // Explicitly from the multipart body: minimal APIs bind bare scalars from the query string,
        // so without this the fields sent alongside the files are silently dropped.
        [FromForm] Guid? activityId,
        [FromForm] string? type,
        CancellationToken cancellationToken)
    {
        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (files.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["files"] = ["Aucun fichier reçu."],
            });
        }

        var oversized = files.Where(file => file.Length > MaxFileSizeBytes).ToList();
        if (oversized.Count > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["files"] = [.. oversized.Select(file =>
                    $"« {file.FileName} » dépasse la limite de 50 Mo par fichier.")],
            });
        }

        var vault = vaultStore.Get(tenant.VaultId);
        var created = new List<object>(files.Count);

        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();

            // Encrypted and content-addressed on the way in; identical content stores once.
            var blob = await vault.Blobs.PutAsync(stream, cancellationToken);

            var document = new Document
            {
                MatterId = matterId,
                ActivityId = activityId,
                BlobSha256 = blob.Sha256,
                FileName = Path.GetFileName(file.FileName),
                SizeBytes = blob.SizeBytes,
                MimeType = file.ContentType,
                Type = type,
            };

            database.Documents.Add(document);
            created.Add(new { document.Id, document.FileName, document.SizeBytes });
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/matters/{matterId}/documents", created);
    }
}
