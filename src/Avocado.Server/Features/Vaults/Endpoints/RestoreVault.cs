using Avocado.Server.Data;
using Avocado.Vault;
using Avocado.Vault.Backups;
using Avocado.Vault.Crypto;

namespace Avocado.Server.Features.Vaults.Endpoints;

/// <param name="Source">The folder holding the backup: a USB key's root, a synced folder, a share.</param>
public sealed record RestoreSourceInput(string Source);

/// <param name="Path">A file the user picked: the sheet saved as a PDF, or the text file on the key.</param>
public sealed record RecoveryFileInput(string Path);

/// <param name="Destination">Where the rebuilt vault goes. Must not already hold one.</param>
public sealed record RestoreInput(
    string Source,
    Guid VaultId,
    string SnapshotPath,
    string Destination,
    string RecoveryCode);

/// <param name="TakenAt">When this copy of the database was made.</param>
public sealed record RestorePointView(string Path, DateTimeOffset TakenAt, long SizeBytes);

/// <param name="Documents">How many documents came with it. Zero would mean records only, which is worth seeing.</param>
public sealed record RestoreCandidateView(
    Guid VaultId,
    DateTimeOffset? UpdatedAt,
    int Documents,
    long DocumentBytes,
    IReadOnlyList<RestorePointView> Points);

/// <summary>
/// The way back onto a machine that has never seen this practice.
///
/// <para>Reachable while the vault is Absent, which is the only time it makes sense, and therefore
/// under <c>/api/vault</c> where <c>VaultReadyMiddleware</c> lets requests through. Everything else
/// answers 503 until a vault is open, and on the morning this runs there is not one.</para>
/// </summary>
public static class RestoreVault
{
    /// <summary>
    /// What a folder holds, before anything is typed. Someone pointing at a USB key should be told
    /// « une sauvegarde du 14 août, 431 documents » rather than being asked for a recovery key on
    /// faith, and told only afterwards that the folder was the wrong one.
    /// </summary>
    public static async Task<IResult> DiscoverAsync(
        RestoreSourceInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Source) || !Directory.Exists(input.Source))
        {
            return Results.Problem(
                title: "Dossier introuvable",
                detail: "Ce dossier n'existe pas ou n'est pas accessible.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "source-missing" });
        }

        var sink = new DirectorySink(new FixedPathLocator(input.Source, "Sauvegarde"));
        var candidates = await Avocado.Vault.Backups.VaultRestore
            .DiscoverAsync(sink, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(candidates.Select(candidate => new RestoreCandidateView(
            candidate.VaultId,
            candidate.UpdatedAt,
            candidate.BlobCount,
            candidate.BlobBytes,
            candidate.Snapshots
                .Select(point => new RestorePointView(point.Path, point.TakenAt, point.SizeBytes))
                .ToList())));
    }

    /// <summary>
    /// Reads the recovery code out of the sheet, so it does not have to be transcribed.
    ///
    /// <para>The file is read here rather than in the window because the renderer is sandboxed and has
    /// no filesystem, which is the arrangement that keeps a bug in the UI away from the vault. It
    /// never leaves this machine either way.</para>
    /// </summary>
    public static IResult ReadRecoveryFile(RecoveryFileInput input)
    {
        if (RecoveryCodeFile.Extract(input.Path) is { } code)
        {
            return Results.Ok(new { code });
        }

        return Results.Problem(
            title: "Clé introuvable dans ce fichier",
            detail: "Ce fichier ne contient pas de clé de récupération lisible. Si c'est bien votre " +
                    "fiche, saisissez les neuf groupes à la main : une fiche scannée ou photographiée " +
                    "est une image, et son texte n'est pas lisible par l'application.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = "no-key-in-file" });
    }

    /// <summary>
    /// Rebuilds it, opens it, and hands the session over so the window can carry on as if the vault
    /// had always been here.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        RestoreInput input,
        VaultSession session,
        VaultDbContextFactory contexts,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var sink = new DirectorySink(new FixedPathLocator(input.Source, "Sauvegarde"));

        try
        {
            var restored = await Avocado.Vault.Backups.VaultRestore.RestoreAsync(
                sink,
                input.VaultId,
                input.SnapshotPath,
                input.Destination,
                input.RecoveryCode,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Adopted before migrating, and the order is not cosmetic: the context factory resolves a
            // vault through the session, so migrating first asks the session for a vault it does not
            // yet have and fails with « le coffre n'est pas ouvert ». Startup has the same shape,
            // resume then migrate.
            session.Adopt(restored);

            // A backup can be older than the application restoring it, so the schema is brought
            // forward before anything reads it. This snapshots first, as every migration does, which
            // on a freshly restored vault costs a file copy and buys a way back.
            await VaultMigrator.EnsureUpToDateAsync(
                restored, contexts, loggers.CreateLogger("Restore"), cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { vaultId = restored.Id, directory = restored.Paths.Root });
        }
        catch (VaultUnlockFailedException)
        {
            // Its own code: this is the one failure the user can fix by looking at their sheet again,
            // and it must not read like a broken backup.
            return Results.Problem(
                title: "Clé de récupération refusée",
                detail: "Cette clé n'ouvre pas cette sauvegarde. Vérifiez les neuf groupes : la saisie " +
                        "ignore la casse et les tirets, mais un groupe manquant suffit à la refuser.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "bad-recovery-key" });
        }
        catch (SyncedFolderException exception)
        {
            return Results.Problem(
                title: "Dossier synchronisé",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "synced-folder" });
        }
        catch (VaultException exception)
        {
            return Results.Problem(
                title: "Restauration impossible",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
