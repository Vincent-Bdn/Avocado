using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Checkout;
using Avocado.Server.Features.Documents.Workspace;
using Avocado.Vault;
using Avocado.Vault.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Settings.Endpoints;

public sealed record WorkingDirectoryInput(string Path);

/// <summary>
/// Moves the folder where dossiers are opened and documents are edited.
///
/// <para>Its own endpoint rather than a line in the practice settings, because it is a property of
/// this computer and not of the cabinet: it is stored on the machine, and a vault restored onto a
/// replacement laptop must not arrive carrying the old one's paths.</para>
/// </summary>
public static class SetWorkingDirectory
{
    public static async Task<IResult> HandleAsync(
        WorkingDirectoryInput input,
        WorkingDirectory working,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        if (working.IsOverridden)
        {
            return Problem(
                "Emplacement imposé",
                "Cet emplacement est fixé au démarrage de l'application et ne peut pas être changé ici.",
                "overridden");
        }

        if (string.IsNullOrWhiteSpace(input.Path))
        {
            return Problem("Aucun dossier", "Choisissez un dossier.", "empty");
        }

        // Refused while anything is open, rather than moving decrypted documents behind her back. It
        // also keeps the move trivial: there is nothing in the old folder worth carrying.
        var open = await database.MatterCheckouts.CountAsync(cancellationToken).ConfigureAwait(false);
        if (open > 0)
        {
            return Problem(
                "Des dossiers sont ouverts",
                $"Refermez d'abord {(open == 1 ? "le dossier ouvert" : $"les {open} dossiers ouverts")} " +
                "sur cet ordinateur, puis changez cet emplacement.",
                "matters-open");
        }

        var chosen = System.IO.Path.GetFullPath(input.Path.Trim());

        // The same refusal the coffre gets, for the opposite reason. A sync client uploading a
        // decrypted dossier would put in the cloud, in the clear, exactly what the vault exists to
        // keep out of it.
        if (CloudSyncDetector.IsInsideSyncedFolder(chosen, out var syncRoot))
        {
            return Problem(
                "Dossier synchronisé",
                $"« {syncRoot} » est synchronisé. Les documents y seraient déchiffrés, donc envoyés en " +
                "clair dans le nuage. Choisissez un dossier local.",
                "synced-folder");
        }

        // Inside the coffre it would be backed up, which is precisely what a decrypted working copy
        // must never be.
        var vaultRoot = System.IO.Path.GetFullPath(vaults.Get(Guid.Empty).Paths.Root);
        if (chosen.StartsWith(vaultRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || chosen.Equals(vaultRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                "Dossier dans le coffre",
                "Les documents déchiffrés ne peuvent pas vivre dans le coffre : ils s'y retrouveraient " +
                "sauvegardés en clair.",
                "inside-vault");
        }

        try
        {
            Directory.CreateDirectory(chosen);

            // Writable, not merely creatable. Finding out otherwise the first time she opens a
            // dossier would be finding out at the worst moment.
            var probe = System.IO.Path.Combine(chosen, $".avocado-write-test-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [], cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Problem("Dossier inaccessible", exception.Message, "not-writable");
        }

        working.MoveTo(chosen);
        return Results.Ok(new { path = working.Root });
    }

    private static IResult Problem(string title, string detail, string code) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
