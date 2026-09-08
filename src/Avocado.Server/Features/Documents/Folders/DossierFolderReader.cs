using Avocado.Server.Features.Mails.Infrastructure;

namespace Avocado.Server.Features.Documents.Folders;

/// <param name="RelativePath">From the dossier's folder, with « / » separators whatever the platform.</param>
/// <param name="ExhibitNumber">Set when Avocado produced this file as a pièce.</param>
public sealed record FolderEntry(
    string Name,
    string RelativePath,
    bool IsFolder,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    bool IsMail,
    int? ExhibitNumber);

/// <param name="Path">Absolute. Null when the dossier has no folder yet.</param>
/// <param name="Missing">The folder is set but not there: a disk unplugged, a folder renamed.</param>
public sealed record FolderListing(
    string? Path,
    bool Missing,
    int Files,
    long Bytes,
    IReadOnlyList<FolderEntry> Entries);

/// <summary>
/// Reads the folder a dossier lives in, as it is on disk.
///
/// <para><b>Avocado no longer owns these files.</b> Ten lawyers said the same thing: they already have
/// a place for their documents, organised as they want it, and an application that took copies into an
/// encrypted store they had to check out of was work rather than help. So the dossier holds a path,
/// the tab shows what is in it, and Explorer is where it is edited.</para>
///
/// <para>Nothing is cached and nothing is indexed. A listing read on demand is always right, which a
/// mirror of somebody else's folder can never be: she will rename things, drop things in from Outlook
/// and reorganise on a Friday afternoon, and none of that should need Avocado to notice.</para>
/// </summary>
public static class DossierFolderReader
{

    public static FolderListing Read(string? folder, string? relative, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new FolderListing(null, false, 0, 0, []);
        }

        var root = Path.GetFullPath(folder);

        if (!Directory.Exists(root))
        {
            return new FolderListing(root, true, 0, 0, []);
        }

        // A path from the client is never trusted to stay inside: « ../../ » would list her home
        // directory through an endpoint that is meant to show one dossier.
        var target = string.IsNullOrWhiteSpace(relative)
            ? root
            : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!Inside(root, target) || !Directory.Exists(target))
        {
            target = root;
        }

        var entries = new List<FolderEntry>();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(target))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(directory);

                if (IsNoise(name))
                {
                    continue;
                }

                var (_, bytes) = Weigh(directory, cancellationToken);

                entries.Add(new FolderEntry(
                    name,
                    Relative(root, directory),
                    true,
                    bytes,
                    Modified(directory),
                    false,
                    null));
            }

            foreach (var file in Directory.EnumerateFiles(target))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(file);

                if (IsNoise(name))
                {
                    continue;
                }

                var info = new FileInfo(file);

                entries.Add(new FolderEntry(
                    name,
                    Relative(root, file),
                    false,
                    info.Length,
                    info.LastWriteTimeUtc,
                    MailFile.LooksLikeMail(file),
                    Exhibits.NumberOf(name)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FolderListing(root, true, 0, 0, []);
        }

        var total = Weigh(root, cancellationToken);

        return new FolderListing(
            root,
            false,
            total.Files,
            total.Bytes,
            // Folders first, then files, each by name the way a file manager orders them: numerically,
            // because she numbers her drawers 01, 02, 10.
            [.. entries
                .OrderByDescending(entry => entry.IsFolder)
                .ThenBy(entry => entry.Name, NaturalOrder.Instance)]);
    }

    /// <summary>
    /// How many files the dossier holds and the few most recently touched, in one walk.
    ///
    /// <para>For the aperçu, which wants « derniers modifiés » and a count and nothing else. Reading
    /// the full listing would weigh every subfolder separately, which is several walks of a tree that
    /// runs to four thousand files in the largest of hers.</para>
    /// </summary>
    public static (int Files, IReadOnlyList<FolderEntry> Recent) Summarise(
        string? folder,
        int take,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return (0, []);
        }

        var root = Path.GetFullPath(folder);
        var files = 0;
        var recent = new List<FolderEntry>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(file);

                if (IsNoise(name))
                {
                    continue;
                }

                files++;

                var info = new FileInfo(file);

                recent.Add(new FolderEntry(
                    name,
                    Relative(root, file),
                    false,
                    info.Length,
                    info.LastWriteTimeUtc,
                    MailFile.LooksLikeMail(file),
                    Exhibits.NumberOf(name)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (files, []);
        }

        return (
            files,
            [.. recent.OrderByDescending(entry => entry.ModifiedAt).Take(take)]);
    }

    private static (int Files, long Bytes) Weigh(string folder, CancellationToken cancellationToken)
    {
        var files = 0;
        long bytes = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsNoise(Path.GetFileName(file)))
                {
                    continue;
                }

                files++;

                try
                {
                    bytes += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return (files, bytes);
    }

    private static DateTimeOffset Modified(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Whether a resolved path is genuinely under the root, symlinks and « .. » included.</summary>
    public static bool Inside(string root, string path)
    {
        var normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return candidate.Equals(normalised, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalised + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Word's lock files and the operating system's own litter. Public because the sauvegarde walks
    /// the same folders for a different reason and must reach the same verdict: a file the Documents
    /// tab hides and the backup carries, or the reverse, is a discrepancy nobody could explain.
    /// </summary>
    public static bool IsNoise(string name) =>
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.Ordinal)
        || name.StartsWith("~$", StringComparison.Ordinal);
}

/// <summary>« 2 Actes » before « 10 Pièces », which plain string order gets backwards.</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? left, string? right) =>
        string.Compare(left, right, StringComparison.CurrentCulture) is var _
            ? Natural(left ?? string.Empty, right ?? string.Empty)
            : 0;

    private static int Natural(string left, string right)
    {
        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                var a = i;
                var b = j;

                while (i < left.Length && char.IsAsciiDigit(left[i])) i++;
                while (j < right.Length && char.IsAsciiDigit(right[j])) j++;

                var first = long.Parse(left[a..i]);
                var second = long.Parse(right[b..j]);

                if (first != second)
                {
                    return first.CompareTo(second);
                }

                continue;
            }

            var order = char.ToLowerInvariant(left[i]).CompareTo(char.ToLowerInvariant(right[j]));

            if (order != 0)
            {
                return order;
            }

            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }
}
