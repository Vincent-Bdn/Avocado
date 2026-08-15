using Avocado.Vault.Storage;

namespace Avocado.Vault.Backups;

/// <param name="FileName">Sortable and stable: <c>yyyyMMdd-HHmmss-label.db</c>.</param>
public readonly record struct VaultSnapshot(string FileName, string FullPath, DateTimeOffset TakenAt, long SizeBytes);

/// <summary>
/// How much history to keep.
///
/// <para>A class rather than a record struct, and the reason is worth keeping. As a struct, its zero
/// value is <c>KeepNewest = 0</c>, which reads as « keep no backups » and deletes the lot, and
/// <c>new SnapshotRetention()</c> silently produces exactly that: a struct's parameterless
/// constructor is not the primary constructor, so the default parameter values never run. A policy
/// whose default value destroys everything is not a policy anyone should be able to reach by
/// accident, least of all in the one part of the program that exists to prevent loss.</para>
/// </summary>
/// <param name="KeepNewest">Always keep this many, however close together they were taken.</param>
/// <param name="KeepDailyForDays">And beyond those, the last of each day for this long.</param>
public sealed record SnapshotRetention(int KeepNewest, int KeepDailyForDays)
{
    public static SnapshotRetention Default { get; } = new(KeepNewest: 12, KeepDailyForDays: 60);

    /// <summary>Belt and braces: whatever a caller or a settings row says, one copy always survives.</summary>
    public int EffectiveKeepNewest => Math.Max(1, KeepNewest);
}

/// <summary>
/// The local snapshot history: dated copies of the database in the vault's own <c>backups/</c> folder.
///
/// <para><b>This is history, not disaster recovery, and the difference matters.</b> A snapshot beside
/// the vault answers "undo what happened this afternoon" and "the migration was wrong". It answers
/// nothing at all about a dead disk or a stolen laptop, because it dies with them. That is what a
/// <see cref="IBackupSink"/> is for, and why the interface never lets the two be confused.</para>
///
/// <para>Snapshots are cheap in a way that is easy to under-use: the database holds records, not
/// documents, so it is megabytes where the blob folder is gigabytes. Keeping months of them costs
/// almost nothing, and the day someone notices a mistake made three weeks ago is the day that turns
/// out to matter.</para>
/// </summary>
public sealed class SnapshotStore(VaultPaths paths)
{
    private readonly string _directory = paths.BackupsDirectory;

    public IReadOnlyList<VaultSnapshot> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "*" + BackupLayout.SnapshotExtension)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var takenAt = BackupLayout.SnapshotTakenAt(name);
                return takenAt is null ? (VaultSnapshot?)null : new VaultSnapshot(name, path, takenAt.Value, new FileInfo(path).Length);
            })
            .OfType<VaultSnapshot>()
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .ToList();
    }

    public VaultSnapshot? Newest() => List() is [var newest, ..] ? newest : null;

    /// <summary>
    /// Deletes what the policy no longer wants, newest first. Returns what was removed.
    ///
    /// <para>The two rules answer two different fears. « Keep the newest N » covers the mistake
    /// noticed within the hour. « Keep one a day for D days » covers the one noticed in a month, and
    /// stops a busy Tuesday from pushing every other day out of the history.</para>
    /// </summary>
    public IReadOnlyList<VaultSnapshot> Prune(SnapshotRetention retention, DateTimeOffset now)
    {
        var all = List();
        var keep = new HashSet<string>(all.Take(retention.EffectiveKeepNewest).Select(snapshot => snapshot.FileName));

        var horizon = now.AddDays(-retention.KeepDailyForDays);
        foreach (var day in all.Where(snapshot => snapshot.TakenAt >= horizon).GroupBy(snapshot => snapshot.TakenAt.UtcDateTime.Date))
        {
            keep.Add(day.OrderByDescending(snapshot => snapshot.TakenAt).First().FileName);
        }

        var removed = new List<VaultSnapshot>();
        foreach (var snapshot in all.Where(snapshot => !keep.Contains(snapshot.FileName)))
        {
            try
            {
                File.Delete(snapshot.FullPath);
                removed.Add(snapshot);
            }
            catch (IOException)
            {
                // Held open by something. It will still be here next time, and one extra old snapshot
                // is not worth failing a backup over.
            }
        }

        return removed;
    }
}
