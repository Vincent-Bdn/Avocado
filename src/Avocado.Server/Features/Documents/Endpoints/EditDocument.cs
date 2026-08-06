using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Workspace;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <summary>
/// « Ouvrir » on a document: decrypt it into the working folder and answer with the path, which the
/// shell then hands to the operating system. Everything after that is the workspace's business.
/// </summary>
public static class EditDocument
{
    public static async Task<IResult> OpenAsync(
        Guid id,
        AvocadoDbContext database,
        DocumentWorkspace workspace,
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

        if (!vault.Blobs.Exists(new BlobReference(document.BlobSha256, document.SizeBytes)))
        {
            return Results.Problem(
                title: "Fichier introuvable dans le coffre",
                detail: $"« {document.FileName} » est référencé mais absent du coffre. " +
                        "Restaurez une sauvegarde complète (base et documents).",
                statusCode: StatusCodes.Status410Gone);
        }

        var path = await workspace.CheckOutAsync(tenant.VaultId, document, cancellationToken);

        return Results.Ok(new { path });
    }

    public static async Task<IResult> CloseAsync(
        Guid id,
        DocumentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await workspace.CheckInAsync(id, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// What is open right now, and what a previous crash left behind. The client polls the first
    /// while anything is open, and shows the second once, at launch.
    /// </summary>
    public static async Task<IResult> StatusAsync(
        DocumentWorkspace workspace,
        TenantContext tenant,
        CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            open = workspace.Status(),
            abandoned = await workspace.AbandonedAsync(tenant.VaultId, cancellationToken),
        });

    public static async Task<IResult> ResolveAsync(
        Guid id,
        bool keep,
        DocumentWorkspace workspace,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        await workspace.ResolveAbandonedAsync(tenant.VaultId, id, keep, cancellationToken);

        return Results.NoContent();
    }
}
