using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <param name="Dossier">The dossier's reference, so the sentence names something she recognises.</param>
/// <param name="Path">What could not be read. A folder when the whole dossier was unreachable.</param>
public sealed record CaptureIssue(string Dossier, string Path, string Reason);

/// <param name="Files">In the sauvegarde afterwards, across every dossier.</param>
/// <param name="Added">Read and stored on this pass. Nearly always small: only what changed.</param>
/// <param name="Dropped">Rows for files she has since deleted or renamed.</param>
/// <param name="Unreachable">Dossiers whose folder was not there. Their files were left alone.</param>
/// <param name="IssueCount">
/// How many there were in total, which is not <c>Issues.Count</c>: the stored report keeps a handful
/// as examples, and « 2 fichiers illisibles » and « 2 400 » call for different reactions.
/// </param>
public sealed record CaptureReport(
    int Dossiers,
    int Files,
    long Bytes,
    int Added,
    long AddedBytes,
    int Dropped,
    int Unreachable,
    int IssueCount,
    IReadOnlyList<CaptureIssue> Issues,
    DateTimeOffset CompletedAt)
{
    public static CaptureReport Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, [], DateTimeOffset.MinValue);
}

/// <summary>
/// Copies the dossiers' folders into the coffre, encrypted, so that a sauvegarde contains the
/// documents and not merely the notes about them.
///
/// <para><b>Why Avocado carries them at all, having just stopped owning them.</b> The two are not in
/// tension. She works in her own folders, on plain files, in Explorer, and nothing here changes that.
/// But a sauvegarde that restored a practice with every dossier empty would be a cruel joke: what a
/// lawyer must be able to produce is the pièce, not the line in the journal saying a pièce exists.
/// Today the copy those ten practices actually keep is a USB key carrying the files in clear, and one
/// left on a métro seat is a bâtonnier's phone call. An encrypted copy is strictly better than that,
/// and it is the only part of this Avocado can do for them.</para>
///
/// <para><b>Cheap on every pass but the first.</b> A file whose size and last-write time match the
/// row is not opened. So the first night reads the practice, twelve gigabytes of it, and every night
/// after that reads the dozen files touched that day. Blobs are content-addressed, so a file moved
/// between two folders stores nothing new, and so does the same attachment filed in four dossiers.</para>
/// </summary>
public sealed class FolderCapture(CaptureProgress progress, TimeProvider clock, ILogger<FolderCapture> logger)
{
    public async Task<CaptureReport> RunAsync(
        IBlobStore blobs,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matters = await database.Matters
            .Where(matter => matter.DocumentsFolder != null)
            .OrderBy(matter => matter.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var issues = new List<CaptureIssue>();
        var added = 0;
        var dropped = 0;
        var unreachable = 0;
        var index = 0;
        long addedBytes = 0;

        progress.Begin(matters.Count);

        try
        {
            foreach (var matter in matters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Advance(++index, matter.Reference, added);

                var outcome = await CaptureAsync(blobs, database, matter, issues, cancellationToken)
                    .ConfigureAwait(false);

                added += outcome.Added;
                addedBytes += outcome.AddedBytes;
                dropped += outcome.Dropped;
                unreachable += outcome.Unreachable ? 1 : 0;

                // Saved per dossier rather than at the end. A capture of a whole practice can take an
                // hour on its first night, and a laptop lid closing in the middle of it must leave
                // behind the dossiers already done rather than nothing at all.
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Including when the lid closed mid-pass. A progress line that never goes away is worse
            // than none: it says the application is doing something it stopped doing hours ago.
            progress.End();
        }

        var held = await database.Set<CapturedFile>()
            .GroupBy(_ => 1)
            .Select(group => new { Files = group.Count(), Bytes = group.Sum(file => file.SizeBytes) })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var report = new CaptureReport(
            matters.Count,
            held?.Files ?? 0,
            held?.Bytes ?? 0,
            added,
            addedBytes,
            dropped,
            unreachable,
            issues.Count,
            issues,
            clock.GetUtcNow());

        logger.LogInformation(
            "Documents captured: {Files} files in {Dossiers} dossiers, {Added} new, {Issues} unreadable.",
            report.Files, report.Dossiers, report.Added, issues.Count);

        return report;
    }

    private async Task<(int Added, long AddedBytes, int Dropped, bool Unreachable)> CaptureAsync(
        IBlobStore blobs,
        AvocadoDbContext database,
        Matter matter,
        List<CaptureIssue> issues,
        CancellationToken cancellationToken)
    {
        var root = matter.DocumentsFolder!;

        var known = await database.Set<CapturedFile>()
            .Where(file => file.MatterId == matter.Id)
            .ToDictionaryAsync(file => file.RelativePath, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        if (!Directory.Exists(root))
        {
            // An external disk unplugged, a network share not mounted, a folder she moved this
            // morning. Whatever it is, it is not « she deleted every document in the dossier », and
            // treating it as such would quietly drop the dossier out of the sauvegarde on the one
            // night nobody was watching. The rows stay; the blobs stay; she is told.
            issues.Add(new CaptureIssue(matter.Reference, root, "Dossier introuvable, rien n'a été changé."));
            return (0, 0, 0, true);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        long addedBytes = 0;

        foreach (var path in Walk(root, matter.Reference, issues))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            seen.Add(relative);

            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;  // Deleted between the walk and here. It will be caught next time.
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new CaptureIssue(matter.Reference, relative, Explain(exception)));
                continue;
            }

            var modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

            if (known.TryGetValue(relative, out var existing)
                && existing.SizeBytes == info.Length
                && existing.ModifiedAt == modifiedAt)
            {
                continue;
            }

            BlobReference blob;
            try
            {
                // Shared for reading and for writing: Word holds an open document open, and refusing
                // to read a file because its author has it on screen would mean the documents she is
                // working on this week are precisely the ones never backed up.
                var source = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024, useAsync: true);

                await using (source.ConfigureAwait(false))
                {
                    blob = await blobs.PutAsync(source, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Named, never swallowed. A file the sauvegarde does not contain is a fact she has to
                // be able to find out, and the previous copy of it stays in the manifest meanwhile.
                issues.Add(new CaptureIssue(matter.Reference, relative, Explain(exception)));
                continue;
            }

            if (existing is null)
            {
                database.Set<CapturedFile>().Add(new CapturedFile
                {
                    MatterId = matter.Id,
                    RelativePath = relative,
                    BlobSha256 = blob.Sha256,
                    SizeBytes = blob.SizeBytes,
                    ModifiedAt = modifiedAt,
                    CapturedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                existing.BlobSha256 = blob.Sha256;
                existing.SizeBytes = blob.SizeBytes;
                existing.ModifiedAt = modifiedAt;
                existing.CapturedAt = clock.GetUtcNow();
            }

            added++;
            addedBytes += blob.SizeBytes;
        }

        var gone = known.Where(entry => !seen.Contains(entry.Key)).Select(entry => entry.Value).ToList();
        database.Set<CapturedFile>().RemoveRange(gone);

        return (added, addedBytes, gone.Count, false);
    }

    /// <summary>
    /// Every file under the dossier, one unreadable subfolder at a time.
    ///
    /// <para><see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> with
    /// <c>AllDirectories</c> would abandon the entire walk on the first folder it cannot open, which
    /// on a machine with one permission-denied subfolder means the dossier is silently not backed up.
    /// Recursing by hand costs a dozen lines and loses only the folder that is actually unreadable.</para>
    /// </summary>
    private static IEnumerable<string> Walk(string root, string reference, List<CaptureIssue> issues)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var folder = pending.Pop();

            string[] files;
            string[] folders;

            try
            {
                files = Directory.GetFiles(folder);
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new CaptureIssue(reference, Path.GetRelativePath(root, folder), Explain(exception)));
                continue;
            }

            foreach (var file in files)
            {
                // The same rule the Documents tab hides them by. ~$conclusions.docx is the lock
                // file Word writes beside an open document: it holds a user name, it vanishes when
                // the document closes, and carrying it would make every night find « new files »
                // that are gone by morning.
                if (DossierFolderReader.IsNoise(Path.GetFileName(file)))
                {
                    continue;
                }

                yield return file;
            }

            foreach (var child in folders)
            {
                pending.Push(child);
            }
        }
    }

    private static string Explain(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Accès refusé.",
        IOException io => io.Message,
        _ => exception.Message,
    };
}
