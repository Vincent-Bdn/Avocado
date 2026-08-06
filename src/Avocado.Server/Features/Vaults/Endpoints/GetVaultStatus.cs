using Avocado.Server.Features.Vaults.Endpoints.Dtos;
using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault.Storage;

namespace Avocado.Server.Features.Vaults.Endpoints;

/// <summary>
/// The first call the renderer makes. Everything else waits on the answer: absent means run the
/// wizard, locked means ask for the recovery key, unlocked means show the application.
/// </summary>
public static class GetVaultStatus
{
    public static IResult Handle(VaultSession session)
    {
        var unlocked = session.State == VaultState.Unlocked;
        var vault = unlocked ? session.Get(Guid.Empty) : null;

        return Results.Ok(new VaultStatusResponse(
            session.State,
            session.Paths.Root,
            session.LockReason,
            vault?.Id,
            vault?.Keyring.HasRecoveryKey ?? false,
            Suggest()));
    }

    /// <summary>
    /// Documents\Avocado unless that sits inside a synced folder, in which case the user profile
    /// root — suggesting a location the next step would reject is worse than not suggesting one.
    /// </summary>
    private static string Suggest()
    {
        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Avocado");

        if (!CloudSyncDetector.IsInsideSyncedFolder(Path.GetDirectoryName(documents)!, out _))
        {
            return documents;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Avocado");
    }
}
