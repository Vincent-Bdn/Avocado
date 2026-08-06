using Avocado.Server.Data;
using Avocado.Server.Features.Vaults.Endpoints.Dtos;
using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault;

namespace Avocado.Server.Features.Vaults.Endpoints;

public static class CreateVault
{
    public static async Task<IResult> HandleAsync(
        VaultCreateRequest request,
        VaultSession session,
        VaultDbContextFactory contextFactory,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
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

        VaultCreation creation;
        try
        {
            creation = session.Create(request.Directory, request.AllowSyncedFolder);
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

        await VaultMigrator.EnsureUpToDateAsync(
            creation.Vault, contextFactory, loggers.CreateLogger("Avocado.Vaults"), cancellationToken);

        // The only time this code exists anywhere. It is not stored, not logged, and cannot be
        // fetched again — the wizard has one chance to get it onto paper or a USB key.
        return Results.Ok(new VaultCreatedResponse(
            creation.Vault.Id,
            creation.Vault.Paths.Root,
            creation.RecoveryCode));
    }
}
