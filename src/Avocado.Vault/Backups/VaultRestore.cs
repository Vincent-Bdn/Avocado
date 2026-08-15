using System.Text.Json;
using Avocado.Vault.Keys;
using Avocado.Vault.Storage;

namespace Avocado.Vault.Backups;

/// <param name="Snapshots">Newest first. Offering the history is the point: the newest copy is not
/// always the one wanted, because the reason for restoring is sometimes that something went wrong.</param>
public sealed record RestoreCandidate(
    Guid VaultId,
    DateTimeOffset? UpdatedAt,
    int BlobCount,
    long BlobBytes,
    IReadOnlyList<RestorePoint> Snapshots);

public sealed record RestorePoint(string Path, DateTimeOffset TakenAt, long SizeBytes);

/// <summary>
/// The way back. Rebuilds a vault folder on a machine that has never seen it, from a destination and
/// the recovery key.
///
/// <para>This is the half of a backup system that is usually written last and tested never. It is
/// written here as a first-class operation, with the whole path in one place, precisely because the
/// day it runs is a bad day: the laptop is gone, the person running it is not calm, and anything that
/// needs improvising will not be improvised correctly.</para>
///
/// <para>The device key is left behind deliberately. It is bound to the machine that is gone and
/// could never be unwrapped here; the recovery key is what proves this is the same person, and the
/// new machine enrolls its own device key at the end.</para>
/// </summary>
public static class VaultRestore
{
    /// <summary>Every vault this destination holds. Usually one, occasionally two after a merger.</summary>
    public static async Task<IReadOnlyList<RestoreCandidate>> DiscoverAsync(
        IBackupSink sink,
        CancellationToken cancellationToken = default)
    {
        var entries = await sink.ListAsync("avocado", cancellationToken).ConfigureAwait(false);

        var candidates = new List<RestoreCandidate>();

        foreach (var group in entries.GroupBy(entry => entry.Path.Split('/') is ["avocado", var id, ..] ? id : null))
        {
            if (group.Key is null || !Guid.TryParse(group.Key, out var vaultId))
            {
                continue;
            }

            var snapshotPrefix = BackupLayout.SnapshotPrefix(vaultId) + "/";
            var snapshots = group
                .Where(entry => entry.Path.StartsWith(snapshotPrefix, StringComparison.Ordinal))
                .Select(entry => (entry, TakenAt: BackupLayout.SnapshotTakenAt(entry.Path)))
                .Where(pair => pair.TakenAt is not null)
                .OrderByDescending(pair => pair.TakenAt!.Value)
                .Select(pair => new RestorePoint(pair.entry.Path, pair.TakenAt!.Value, pair.entry.SizeBytes))
                .ToList();

            // Without the keyring there is nothing to offer: the snapshots would be undecryptable and
            // showing them would only invite someone to spend an hour finding that out.
            if (snapshots.Count == 0 || group.All(entry => entry.Path != BackupLayout.Keyring(vaultId)))
            {
                continue;
            }

            var manifest = await ReadManifestAsync(sink, vaultId, cancellationToken).ConfigureAwait(false);
            var blobPrefix = BackupLayout.BlobPrefix(vaultId) + "/";
            var blobs = group.Where(entry => entry.Path.StartsWith(blobPrefix, StringComparison.Ordinal)).ToList();

            candidates.Add(new RestoreCandidate(
                vaultId,
                manifest?.UpdatedAt,
                blobs.Count,
                blobs.Sum(entry => entry.SizeBytes),
                snapshots));
        }

        return candidates.OrderByDescending(candidate => candidate.UpdatedAt).ToList();
    }

    /// <summary>
    /// Rebuilds the vault into <paramref name="destinationDirectory"/> and opens it with the recovery
    /// key, enrolling this machine's device key so the next launch just works.
    ///
    /// <para>The recovery key is checked against the keyring before a single byte of document is
    /// pulled down, because getting it wrong after a two-hour download of somebody's scans is a
    /// cruelty, and the keyring is the first thing fetched anyway.</para>
    /// </summary>
    public static async Task<OpenVault> RestoreAsync(
        IBackupSink sink,
        Guid vaultId,
        string snapshotPath,
        string destinationDirectory,
        string recoveryCode,
        IDeviceKeyStore? deviceKeyStore = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var paths = new VaultPaths(destinationDirectory);

        if (paths.Exists)
        {
            throw new VaultException(
                $"Un coffre existe déjà dans « {paths.Root} ». Choisissez un dossier vide pour la restauration.");
        }

        // Same rule as a new vault: a live SQLite database inside a sync client's folder gets
        // corrupted. Restoring into one would produce a vault that works for a fortnight.
        CloudSyncDetector.ThrowIfInsideSyncedFolder(paths.Root);

        progress?.Report(new BackupProgress("Clés", 0, 1));

        // The key is checked against a copy in the temp folder, and the destination is not touched
        // until it passes.
        //
        // This used to write the keyring into the destination and check afterwards, which meant one
        // mistyped recovery key left a vault.json behind, and `paths.Exists` is exactly that file. The
        // retry was then refused with "un coffre existe déjà" and the folder could never be used
        // again. On a day when someone has lost their computer and is typing fifty-four characters
        // off a printed sheet, one typo is not an unlikely event, and it must cost nothing.
        var staged = Path.Combine(Path.GetTempPath(), $"avocado-restore-{Guid.NewGuid():N}.json");

        try
        {
            await FetchAsync(sink, BackupLayout.Keyring(vaultId), staged, cancellationToken).ConfigureAwait(false);

            using (var probe = VaultKeyring.Load(staged).UnlockWithRecoveryCode(recoveryCode))
            {
            }

            paths.EnsureDirectories();
            File.Copy(staged, paths.KeyringFile, overwrite: true);
        }
        finally
        {
            // Wrapped keys and salts, useless without the recovery key, but there is no reason to
            // leave them in the temp folder either.
            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
            }
        }

        progress?.Report(new BackupProgress("Clés", 1, 1));

        var blobPrefix = BackupLayout.BlobPrefix(vaultId) + "/";
        var blobs = (await sink.ListAsync(BackupLayout.BlobPrefix(vaultId), cancellationToken).ConfigureAwait(false))
            .Where(entry => entry.Path.StartsWith(blobPrefix, StringComparison.Ordinal))
            .ToList();

        var done = 0;
        foreach (var blob in blobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress("Documents", done, blobs.Count));

            var relative = blob.Path[blobPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            await FetchAsync(sink, blob.Path, Path.Combine(paths.BlobsDirectory, relative), cancellationToken)
                .ConfigureAwait(false);

            done++;
        }

        progress?.Report(new BackupProgress("Documents", blobs.Count, blobs.Count));

        progress?.Report(new BackupProgress("Base", 0, 1));
        await FetchAsync(sink, snapshotPath, paths.DatabaseFile, cancellationToken).ConfigureAwait(false);
        progress?.Report(new BackupProgress("Base", 1, 1));

        var vault = VaultManager.UnlockWithRecoveryCode(paths.Root, recoveryCode);

        try
        {
            // Without this the restored vault opens only by typing the recovery key, every launch,
            // which is not a vault anyone would keep using.
            vault.EnrollDeviceKey(deviceKeyStore ?? DeviceKeyStore.ForCurrentPlatform());
            return vault;
        }
        catch
        {
            vault.Dispose();
            throw;
        }
    }

    private static async Task<BackupManifest?> ReadManifestAsync(
        IBackupSink sink,
        Guid vaultId,
        CancellationToken cancellationToken)
    {
        try
        {
            var stream = await sink.OpenReadAsync(BackupLayout.Manifest(vaultId), cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or VaultException)
        {
            // A destination written by an older version, or one whose manifest did not survive. The
            // snapshots are what matter and they are listed independently.
            return null;
        }
    }

    private static async Task FetchAsync(
        IBackupSink sink,
        string sinkPath,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);

        var source = await sink.OpenReadAsync(sinkPath, cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var file = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);

            await using (file.ConfigureAwait(false))
            {
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
