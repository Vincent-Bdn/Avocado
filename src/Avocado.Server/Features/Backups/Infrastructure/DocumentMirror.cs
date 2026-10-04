using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Backups;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <param name="Dossier">The dossier's reference, so the sentence names something she recognises.</param>
/// <param name="Path">What could not be read, relative to the dossier's folder.</param>
public sealed record CaptureIssue(string Dossier, string Path, string Reason);

/// <param name="MatterId">So the window can offer to detach exactly this one.</param>
/// <param name="Dossier">Its reference, which is what she recognises.</param>
/// <param name="Folder">Where Avocado was told to look and found nothing.</param>
public sealed record DetachedFolder(Guid MatterId, string Dossier, string Folder);

/// <param name="Dossiers">How many had a folder that was there and got mirrored.</param>
/// <param name="Copied">Files sent on this pass. Nearly always small: only what changed.</param>
/// <param name="Missing">Dossiers whose folder was not found. Nothing was removed for them.</param>
/// <param name="Unreadable">Files that could not be read, usually because something had them open.</param>
public sealed record MirrorReport(
    int Dossiers,
    int Copied,
    long CopiedBytes,
    int Files,
    long Bytes,
    int Unreadable,
    IReadOnlyList<DetachedFolder> Missing,
    IReadOnlyList<CaptureIssue> Issues)
{
    /// <summary>
    /// Empty rather than null, whatever the JSON said.
    ///
    /// <para>This record is stored as JSON in a settings row and read back by a version that may
    /// have gained fields since it was written. A property absent from the document does not fail to
    /// deserialize: it arrives as null, through a reference the type says is never null, and the
    /// screen that reads it crashes on <c>.Count</c>. <see cref="Missing"/> did exactly that to
    /// every practice whose last capture predated it.</para>
    ///
    /// <para>So the collections are normalised here rather than at each reader. A type that cannot
    /// hold the wrong thing needs nobody to remember.</para>
    /// </summary>
    public IReadOnlyList<DetachedFolder> Missing { get; init; } = Missing ?? [];

    /// <inheritdoc cref="Missing"/>
    public IReadOnlyList<CaptureIssue> Issues { get; init; } = Issues ?? [];

    public static MirrorReport Empty { get; } = new(0, 0, 0, 0, 0, 0, [], []);
}

/// <summary>
/// Copies the dossiers' folders onto a destination as ordinary files.
///
/// <para><b>In clear, deliberately.</b> Avocado does not own her documents: they live in her own
/// folders, under her own names, and the application reads them without moving them. Carrying them
/// into the sauvegarde as encrypted blobs was consistent with the coffre and wrong for the documents,
/// because the NAS is somewhere she goes to look things up. A cabinet opens last year's dossier on
/// the share and reads it; a content-addressed blob store answers that with nothing. So the coffre
/// stays encrypted, and the documents arrive as the files they are.</para>
///
/// <para><b>It never deletes.</b> One direction only, local to destination. A lawyer's laptop holds
/// what is current and the NAS holds everything, which means a dossier absent locally is the normal
/// state of an old dossier and not an instruction to erase it. There is no code path here that
/// removes anything from a destination, and that is a property worth keeping rather than an
/// oversight: see <see cref="MirrorReport.Missing"/> for what happens instead.</para>
/// </summary>
public static class DocumentMirror
{
    /// <summary>Where the dossiers' folders live on a destination, beside the encrypted coffre.</summary>
    public const string Root = "Dossiers";

    /// <summary>
    /// « Dossiers/700770-COULEYRE - EDF ENR ».
    ///
    /// <para>By reference rather than by the name of her local folder, so renaming a folder on her
    /// disk does not strand the old copy on the NAS and start a second one beside it.</para>
    /// </summary>
    public static string FolderFor(string reference, string? name)
    {
        var left = Exhibits.Safe(reference);
        var right = Exhibits.Safe(name ?? string.Empty);

        var label = right.Length == 0 || left.Length == 0 ? left + right : $"{left} - {right}";

        return $"{Root}/{(label.Length == 0 ? "sans-reference" : label)}";
    }

    public static async Task<MirrorReport> RunAsync(
        IBackupSink sink,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matters = await database.Matters
            .AsNoTracking()
            .Where(matter => matter.DocumentsFolder != null)
            .Select(matter => new { matter.Id, matter.Reference, matter.Name, matter.DocumentsFolder })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missing = new List<DetachedFolder>();
        var issues = new List<CaptureIssue>();
        var dossiers = 0;
        var copied = 0;
        var copiedBytes = 0L;
        var files = 0;
        var bytes = 0L;
        var unreadable = 0;

        foreach (var matter in matters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = matter.DocumentsFolder!;

            if (!Directory.Exists(source))
            {
                // Her folder is not here. It may be archived, on a disk she left at home, or genuinely
                // gone. Whichever it is, the copy already on the destination is the only one left, so
                // nothing is touched and the dossier is named for her to decide about.
                missing.Add(new DetachedFolder(matter.Id, matter.Reference, source));
                continue;
            }

            dossiers += 1;

            var prefix = FolderFor(matter.Reference, matter.Name);
            var already = (await sink.ListAsync(prefix, cancellationToken).ConfigureAwait(false))
                .ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in Walk(source, matter.Reference, issues, ref unreadable))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(source, file.FullName).Replace('\\', '/');
                var target = $"{prefix}/{relative}";

                files += 1;
                bytes += file.Length;

                // The destination is the record of what is already there, rather than a table here
                // that could drift from it. A file she deleted from the NAS by hand comes back on the
                // next pass, which is the behaviour a backup should have.
                if (already.TryGetValue(target, out var there)
                    && there.SizeBytes == file.Length
                    && there.ModifiedAt >= file.LastWriteTimeUtc)
                {
                    continue;
                }

                try
                {
                    var reading = new FileStream(
                        file.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        // Word keeps its own document open while she edits it, and a pièce being
                        // written is still worth copying on the next pass rather than failing now.
                        FileShare.ReadWrite | FileShare.Delete);

                    await using (reading.ConfigureAwait(false))
                    {
                        await sink.WriteAsync(target, reading, cancellationToken).ConfigureAwait(false);
                    }

                    copied += 1;
                    copiedBytes += file.Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    unreadable += 1;
                    if (issues.Count < 8)
                    {
                        issues.Add(new CaptureIssue(matter.Reference, relative, exception.Message));
                    }
                }
            }
        }

        return new MirrorReport(
            dossiers, copied, copiedBytes, files, bytes, unreadable, missing, issues);
    }

    /// <summary>
    /// Every file under the dossier's folder, skipping what is not hers to copy.
    ///
    /// <para>Enumerated a directory at a time rather than with AllDirectories, so one unreadable
    /// subfolder costs that subfolder and not the whole dossier.</para>
    /// </summary>
    private static IEnumerable<FileInfo> Walk(
        string root,
        string reference,
        List<CaptureIssue> issues,
        ref int unreadable)
    {
        var found = new List<FileInfo>();
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var folder = pending.Pop();

            try
            {
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    var name = Path.GetFileName(child);

                    // What the operating system and the sync clients leave lying about. Copying
                    // these onto a NAS is noise at best and a conflict storm at worst.
                    if (name.StartsWith('.') || name is "__MACOSX" or "$RECYCLE.BIN" or "System Volume Information")
                    {
                        continue;
                    }

                    pending.Push(child);
                }

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var name = Path.GetFileName(file);

                    if (name is ".DS_Store" or "Thumbs.db" or "desktop.ini" || name.StartsWith("~$", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    found.Add(new FileInfo(file));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable += 1;
                if (issues.Count < 8)
                {
                    issues.Add(new CaptureIssue(reference, folder, exception.Message));
                }
            }
        }

        return found;
    }
}
