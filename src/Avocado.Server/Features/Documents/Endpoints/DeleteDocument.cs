using Avocado.Server.Data;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

public static class DeleteDocument
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        database.Documents.Remove(document);
        await database.SaveChangesAsync(cancellationToken);

        // Blobs are content-addressed and deduplicated, so the same bytes may back another document —
        // the same attachment forwarded twice, or the same scan filed on two matters. Only drop the
        // blob once nothing references it.
        var stillReferenced = await database.Documents
            .AnyAsync(other => other.BlobSha256 == document.BlobSha256, cancellationToken);

        if (!stillReferenced)
        {
            vaultStore.Get(tenant.VaultId).Blobs
                .Delete(new BlobReference(document.BlobSha256, document.SizeBytes));
        }

        return Results.NoContent();
    }
}
