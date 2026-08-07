using Avocado.Server.Data;
using Avocado.Server.Features.Vaults.Endpoints.Dtos;
using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault;

namespace Avocado.Server.Features.Vaults.Endpoints;

/// <summary>
/// The two halves of creating a vault, deliberately separated.
/// <para>
/// <b>Prepare</b> validates the destination and generates the keys in memory, writing nothing.
/// <b>Commit</b> is the first moment anything exists on disk, and only runs once the user has been
/// through the whole wizard. Going back from the recovery step therefore leaves no folder behind, and
/// no Back button has to delete anything, which is not a thing a Back button should do.
/// </para>
/// </summary>
public static class CreateVault
{
    public static IResult Prepare(VaultCreateRequest request, VaultSession session)
    {
        if (string.IsNullOrWhiteSpace(request.Directory))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["directory"] = ["Choisissez un dossier pour le coffre."],
            });
        }

        if (session.State == VaultState.Unlocked)
        {
            return Results.Problem(
                title: "Coffre déjà ouvert",
                detail: "Un coffre est déjà ouvert dans cette session.",
                statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            return Results.Ok(new VaultPreparedResponse(
                session.Prepare(request.Directory, request.AllowSyncedFolder)));
        }
        catch (SyncedFolderException exception)
        {
            // A distinct code so the wizard can render its own French copy and offer the override,
            // rather than pattern-matching the text of an English exception.
            return Results.Problem(
                title: "Dossier synchronisé",
                detail: $"« {exception.DetectedRoot} » est synchronisé par un service de sauvegarde en ligne. "
                        + "Avocado écrit en continu dans sa base ; un logiciel de synchronisation copie les "
                        + "fichiers pendant l'écriture et finit par les abîmer. Placez le coffre sur un disque "
                        + "local, et faites pointer les sauvegardes vers le dossier synchronisé.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "synced-folder" });
        }
        catch (VaultException exception)
        {
            // An existing vault, or a database whose keyring was lost. Both carry a message specific
            // enough to show as-is, and both are rare enough not to warrant their own copy yet.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["directory"] = [exception.Message],
            });
        }
    }

    /// <summary>Abandons the prepared keys. Nothing was written, so nothing is removed.</summary>
    public static IResult Discard(VaultSession session)
    {
        session.DiscardPending();
        return Results.NoContent();
    }

    public static async Task<IResult> CommitAsync(
        VaultSession session,
        VaultDbContextFactory contextFactory,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (!session.HasPending)
        {
            return Results.Problem(
                title: "Rien à créer",
                detail: "Aucun coffre n'a été préparé. Reprenez le choix du dossier.",
                statusCode: StatusCodes.Status409Conflict);
        }

        VaultCreation creation;
        try
        {
            creation = session.Commit();
        }
        catch (VaultException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["directory"] = [exception.Message],
            });
        }

        await VaultMigrator.EnsureUpToDateAsync(
            creation.Vault, contextFactory, loggers.CreateLogger("Avocado.Vaults"), cancellationToken);

        return Results.Ok(new VaultCreatedResponse(creation.Vault.Id, creation.Vault.Paths.Root));
    }
}
