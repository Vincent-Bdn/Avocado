using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avocado.Vault.Backups;

/// <param name="Stage">For the window: « Documents », « Base », « Nettoyage ».</param>
public readonly record struct BackupProgress(string Stage, int Done, int Total);

/// <param name="Skipped">True when the destination was simply not connected. Not a failure.</param>
public sealed record BackupOutcome(
    bool Skipped,
    int BlobsUploaded,
    long BytesUploaded,
    int SnapshotsUploaded,
    int SnapshotsPruned,
    string? SnapshotName,
    DateTimeOffset CompletedAt);

/// <summary>
/// What a destination is told, on the destination, in the clear. Anonymous on purpose: this file sits
/// in someone's Google Drive, and the cabinet's name, the client list and the machine it came from are
/// nobody's business there. A vault id, some dates and some counts are enough for a restore screen to
/// say what it is offering, and they say nothing about whose practice it is.
/// </summary>
public sealed class BackupManifest
{
    [JsonPropertyName("vaultId")] public Guid VaultId { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("latestSnapshot")] public string? LatestSnapshot { get; init; }
    [JsonPropertyName("snapshotCount")] public int SnapshotCount { get; init; }
    [JsonPropertyName("blobCount")] public int BlobCount { get; init; }
    [JsonPropertyName("blobBytes")] public long BlobBytes { get; init; }

    /// <summary>Written for the human, not for us. Nothing reads it back.</summary>
    [JsonPropertyName("readme")]
    public string Readme { get; init; } =
        "Sauvegarde Avocado. Tout le contenu est chiffré et ne peut être ouvert qu'avec la clé de " +
        "récupération du cabinet. Pour restaurer : installez Avocado sur la nouvelle machine et " +
        "choisissez « Restaurer une sauvegarde ».";
}

/// <summary>
/// Copies a vault onto a <see cref="IBackupSink"/>, and keeps doing it cheaply.
///
/// <para><b>Why the sink holds a mirror rather than an archive.</b> Blobs are content-addressed and
/// immutable: a document's file name is derived from its contents, so a blob is written once, ever,
/// and can never need replacing. That makes an incremental push nearly free, which is the only reason
/// backing up gigabytes of scans to a USB key or a Drive folder is practical at all. An archive would
/// re-upload the entire practice every time, which in reality means it gets switched off.</para>
///
/// <para>It also means the thing on the key is legible. Three pieces the way the vault has them, so a
/// restore is comprehensible and, if it ever comes to it, doable by hand.</para>
/// </summary>
public sealed class BackupMirror(OpenVault vault, IBackupSink sink)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Pushes everything the destination is missing.
    ///
    /// <para><b>The order is the correctness.</b> Snapshot first, then blobs, then the snapshot goes
    /// up last. A snapshot taken at T references only blobs that existed at T, and the blob sweep runs
    /// after T, so it necessarily covers them. Push the blobs first instead and a document saved
    /// between the sweep and the snapshot is referenced by a backup that does not contain it, which is
    /// discovered by the person restoring it, a year later, on a new machine, when nothing can be done
    /// about it.</para>
    ///
    /// <para>Which is also why the snapshot is uploaded last: at every instant, the newest snapshot on
    /// the destination is one whose documents are already there.</para>
    /// </summary>
    public async Task<BackupOutcome> PushAsync(
        VaultSnapshot snapshot,
        SnapshotRetention retention,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var probe = await sink.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!probe.IsReady)
        {
            return new BackupOutcome(true, 0, 0, 0, 0, null, DateTimeOffset.UtcNow);
        }

        var vaultId = vault.Id;

        // The keyring first, and every time. It is a couple of kilobytes, and it is the one file
        // without which none of the rest can ever be opened again. Re-writing it also carries a
        // regenerated recovery key to the destination, so a backup can never be locked by a key the
        // vault no longer accepts.
        await WriteFileAsync(BackupLayout.Keyring(vaultId), vault.Paths.KeyringFile, cancellationToken)
            .ConfigureAwait(false);

        var (uploaded, bytes) = await PushBlobsAsync(vaultId, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new BackupProgress("Base", 0, 1));
        await WriteFileAsync(BackupLayout.Snapshot(vaultId, snapshot.FileName), snapshot.FullPath, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new BackupProgress("Base", 1, 1));

        progress?.Report(new BackupProgress("Nettoyage", 0, 1));
        var pruned = await PruneAsync(vaultId, retention, cancellationToken).ConfigureAwait(false);

        var remaining = await sink.ListAsync(BackupLayout.SnapshotPrefix(vaultId), cancellationToken).ConfigureAwait(false);
        var blobs = await sink.ListAsync(BackupLayout.BlobPrefix(vaultId), cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(
            BackupLayout.Manifest(vaultId),
            new BackupManifest
            {
                VaultId = vaultId,
                UpdatedAt = DateTimeOffset.UtcNow,
                LatestSnapshot = remaining.Count == 0 ? null : remaining.Max(entry => entry.Path),
                SnapshotCount = remaining.Count,
                BlobCount = blobs.Count,
                BlobBytes = blobs.Sum(entry => entry.SizeBytes),
            },
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new BackupProgress("Nettoyage", 1, 1));

        return new BackupOutcome(false, uploaded, bytes, 1, pruned, snapshot.FileName, DateTimeOffset.UtcNow);
    }

    private async Task<(int Uploaded, long Bytes)> PushBlobsAsync(
        Guid vaultId,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var present = (await sink.ListAsync(BackupLayout.BlobPrefix(vaultId), cancellationToken).ConfigureAwait(false))
            .ToDictionary(entry => entry.Path, entry => entry.SizeBytes, StringComparer.Ordinal);

        var pending = LocalBlobs()
            .Where(blob => !present.TryGetValue(BackupLayout.Blob(vaultId, blob.Relative), out var size)
                           || size != blob.Length)
            .ToList();

        var uploaded = 0;
        long bytes = 0;

        foreach (var blob in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress("Documents", uploaded, pending.Count));

            var source = File.OpenRead(blob.FullPath);
            await using (source.ConfigureAwait(false))
            {
                await sink.WriteAsync(BackupLayout.Blob(vaultId, blob.Relative), source, cancellationToken)
                    .ConfigureAwait(false);
            }

            uploaded++;
            bytes += blob.Length;
        }

        progress?.Report(new BackupProgress("Documents", pending.Count, pending.Count));
        return (uploaded, bytes);
    }

    /// <summary>
    /// A size mismatch counts as missing, which is what recovers from a key pulled out mid-write on a
    /// destination that cannot write atomically. Contents are never compared: the name already is the
    /// contents, so equal name and equal length is as strong a statement as re-reading the file.
    /// </summary>
    private IEnumerable<(string Relative, string FullPath, long Length)> LocalBlobs()
    {
        var root = vault.Paths.BlobsDirectory;
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.blob", SearchOption.AllDirectories))
        {
            // The blob store's scratch folder, holding half-written files that are about to be moved
            // into place or deleted. Never anything a backup should carry.
            if (path.Contains($"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (Path.GetRelativePath(root, path).Replace('\\', '/'), path, new FileInfo(path).Length);
        }
    }

    private async Task<int> PruneAsync(Guid vaultId, SnapshotRetention retention, CancellationToken cancellationToken)
    {
        var snapshots = (await sink.ListAsync(BackupLayout.SnapshotPrefix(vaultId), cancellationToken).ConfigureAwait(false))
            .Select(entry => (entry.Path, TakenAt: BackupLayout.SnapshotTakenAt(entry.Path)))
            .Where(entry => entry.TakenAt is not null)
            .OrderByDescending(entry => entry.TakenAt!.Value)
            .ToList();

        var keep = new HashSet<string>(snapshots.Take(retention.EffectiveKeepNewest).Select(entry => entry.Path), StringComparer.Ordinal);

        var horizon = DateTimeOffset.UtcNow.AddDays(-retention.KeepDailyForDays);
        foreach (var day in snapshots.Where(entry => entry.TakenAt >= horizon).GroupBy(entry => entry.TakenAt!.Value.UtcDateTime.Date))
        {
            keep.Add(day.First().Path);
        }

        var pruned = 0;
        foreach (var entry in snapshots.Where(entry => !keep.Contains(entry.Path)))
        {
            await sink.DeleteAsync(entry.Path, cancellationToken).ConfigureAwait(false);
            pruned++;
        }

        return pruned;
    }

    private async Task WriteFileAsync(string sinkPath, string localPath, CancellationToken cancellationToken)
    {
        var source = File.OpenRead(localPath);
        await using (source.ConfigureAwait(false))
        {
            await sink.WriteAsync(sinkPath, source, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteJsonAsync<T>(string sinkPath, T value, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Format)));
        await sink.WriteAsync(sinkPath, content, cancellationToken).ConfigureAwait(false);
    }
}
