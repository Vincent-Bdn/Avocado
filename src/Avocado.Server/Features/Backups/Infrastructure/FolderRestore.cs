using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <param name="Reference">The dossier.</param>
/// <param name="Folder">Where its documents were written, so she can go and look.</param>
public sealed record RestoredDossier(string Reference, string Name, string Folder, int Files, long Bytes);

public sealed record RestoreDocumentsOutcome(
    IReadOnlyList<RestoredDossier> Dossiers,
    int Files,
    long Bytes,
    IReadOnlyList<CaptureIssue> Issues);

/// <summary>
/// Puts the documents back on disk, into folders she chooses, and re-points each dossier at its own.
///
/// <para><b>Why she chooses, rather than the paths coming back as they were.</b> The manifest holds
/// relative paths on purpose. <c>C:\Users\Marie\Dossiers\Durand</c> means nothing on the replacement
/// Mac, the old machine's user name may not exist, the external disk may now be a different letter,
/// and writing to an absolute path recorded a year ago is how a restore scatters files into places
/// nobody looks. Two folders, « en cours » and « clôturés », are the whole of what she has to decide,
/// and they may be the same folder. Everything below them keeps the shape she filed it in.</para>
///
/// <para><b>Nothing is overwritten, ever.</b> A restore is run by someone who is not calm, sometimes
/// twice, sometimes into a folder that already holds work. A file already there with the same length
/// is left alone; anything else is written beside it. Losing a document to the operation whose entire
/// purpose is not losing documents would be the worst bug in the application.</para>
/// </summary>
public sealed class FolderRestore(IBlobStore blobs, ILogger<FolderRestore> logger)
{
    public async Task<RestoreDocumentsOutcome> RunAsync(
        AvocadoDbContext database,
        string ongoingRoot,
        string closedRoot,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ongoingRoot);
        Directory.CreateDirectory(closedRoot);

        var matters = await database.Matters
            .OrderBy(matter => matter.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var manifest = await database.Set<CapturedFile>()
            .GroupBy(file => file.MatterId)
            .ToDictionaryAsync(group => group.Key, group => group.ToList(), cancellationToken)
            .ConfigureAwait(false);

        var total = manifest.Sum(entry => entry.Value.Count);
        var done = 0;

        var issues = new List<CaptureIssue>();
        var restored = new List<RestoredDossier>();
        var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        long bytes = 0;

        foreach (var matter in matters)
        {
            if (!manifest.TryGetValue(matter.Id, out var files) || files.Count == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var root = matter.IsOpen ? ongoingRoot : closedRoot;
            var folder = Path.Combine(root, FolderNameFor(matter, root, taken));
            Directory.CreateDirectory(folder);

            var written = 0;
            long writtenBytes = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await WriteAsync(folder, file, matter.Reference, issues, cancellationToken).ConfigureAwait(false))
                {
                    written++;
                    writtenBytes += file.SizeBytes;
                }

                progress?.Report((++done, total));
            }

            // The dossier now points where its documents actually are. Without this the Documents
            // tab would go on naming a folder on a computer that no longer exists.
            matter.DocumentsFolder = folder;

            restored.Add(new RestoredDossier(matter.Reference, matter.Name, folder, written, writtenBytes));
            bytes += writtenBytes;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Restored {Files} documents into {Dossiers} dossiers, {Issues} failures.",
            restored.Sum(dossier => dossier.Files), restored.Count, issues.Count);

        return new RestoreDocumentsOutcome(restored, restored.Sum(dossier => dossier.Files), bytes, issues);
    }

    private async Task<bool> WriteAsync(
        string folder,
        CapturedFile file,
        string reference,
        List<CaptureIssue> issues,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(
            Path.Combine(folder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        // The manifest comes out of a database file that arrived from a USB key, so its paths are
        // checked like any other input: « ../../ » in a relative path would write outside the folder
        // she chose, and a restore is running with her full permissions.
        if (!DossierFolderReader.Inside(folder, destination))
        {
            issues.Add(new CaptureIssue(reference, file.RelativePath, "Chemin refusé."));
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination) && new FileInfo(destination).Length == file.SizeBytes)
            {
                // Already there, same size: a second run of an interrupted restore. Silent by design.
                return true;
            }

            var target = Free(destination);

            var source = blobs.OpenRead(new BlobReference(file.BlobSha256, file.SizeBytes));
            await using (source.ConfigureAwait(false))
            {
                var output = new FileStream(
                    target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 64 * 1024, useAsync: true);

                await using (output.ConfigureAwait(false))
                {
                    await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Vault.VaultException)
        {
            // A blob the destination never received, a disk that filled up, a name the file system
            // refuses. Named, one line each, and the rest of the restore carries on: getting eleven
            // dossiers back and being told about the twelfth beats stopping at the first problem.
            issues.Add(new CaptureIssue(reference, file.RelativePath, exception.Message));
            return false;
        }
    }

    /// <summary>
    /// « Durand » again, not « 2026-0001 ».
    ///
    /// <para>She recognises her own folder names, so the leaf of the original path is what comes back,
    /// and only when two dossiers would collide, or the original is unusable, does the reference get
    /// involved. Under the same roof for « en cours » and « clôturés » if she picked the same folder
    /// twice, which is why the names taken are tracked per root rather than globally.</para>
    /// </summary>
    private static string FolderNameFor(Matter matter, string root, Dictionary<string, HashSet<string>> taken)
    {
        var used = taken.TryGetValue(root, out var existing)
            ? existing
            : taken[root] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var original = matter.DocumentsFolder is { Length: > 0 } path
            ? Path.GetFileName(path.TrimEnd('/', '\\'))
            : null;

        var name = Exhibits.Safe(original ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name))
        {
            // A dossier pointed at a drive root, or at a folder whose name was nothing but
            // punctuation. The reference is always usable and always unique.
            name = matter.Reference;
        }

        if (used.Add(name))
        {
            return name;
        }

        var disambiguated = $"{name} ({matter.Reference})";
        used.Add(disambiguated);

        return disambiguated;
    }

    /// <summary>« Conclusions.docx », then « Conclusions (restauré).docx ». Nothing is written over.</summary>
    private static string Free(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 1; index < 1000; index++)
        {
            var candidate = Path.Combine(
                folder,
                index == 1 ? $"{stem} (restauré){extension}" : $"{stem} (restauré {index}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }
}
