using Avocado.Server.Data;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <summary>
/// Streams the decrypted file. Backs both the preview panel and « Ouvrir » / « Télécharger ».
/// </summary>
public static class DownloadDocument
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        var vault = vaultStore.Get(tenant.VaultId);
        var reference = new BlobReference(document.BlobSha256, document.SizeBytes);

        if (!vault.Blobs.Exists(reference))
        {
            // The row survived but the blob did not. Say so plainly rather than returning a 500:
            // this is the shape of a restore that brought back the database without blobs/.
            return Results.Problem(
                title: "Fichier introuvable dans le coffre",
                detail: $"« {document.FileName} » est référencé mais absent du coffre. " +
                        "Restaurez une sauvegarde complète (base et documents).",
                statusCode: StatusCodes.Status410Gone);
        }

        // Decrypts chunk by chunk as it is read, so a 50 Mo scan never lands in memory whole.
        var stream = vault.Blobs.OpenRead(reference);

        return Results.Stream(
            stream,
            document.MimeType ?? "application/octet-stream",
            document.FileName,
            enableRangeProcessing: false);
    }
}
