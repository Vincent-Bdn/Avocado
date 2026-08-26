using Avocado.Server.Features.Mails.Infrastructure;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <summary>
/// Reads a Gestisoft export off disk and works out what importing it would produce.
///
/// <para>The export is folders and nothing else. Two directories at the top, EN COURS and CLASSES,
/// each holding one folder per client. There is no contacts file, no billing, no metadata: the
/// client's name is the folder's name, and the dossier's state is which of the two it sits in. That is
/// the whole of the structured data, and deriving more of it from filenames would be guessing dressed
/// up as import.</para>
///
/// <para><b>One client folder becomes one dossier, and splitting is offered rather than applied.</b>
/// Some clients hold several affaires in subfolders: ANODEA has two, DLT GROUP has two. Others use
/// subfolders for filing, PHILEAS LOUNGE numbers them 01 Courriers, 02 Procédure, 07 Facturation.
/// Nothing in the folder separates the two cases reliably, both being a handful of directories with
/// human names. Guessing wrong scatters one matter across four, or merges two unrelated affaires, and
/// neither is obvious afterwards. So the default never invents structure, the subtree is preserved as
/// document folders, and where a split looks plausible it is put to the user as a question about her
/// own dossiers, which she can answer and this cannot.</para>
/// </summary>
public static class GestisoftScan
{
    /// <summary>The two folders Gestisoft exports into, and what each says about a dossier.</summary>
    private static readonly (string Folder, bool IsOpen)[] Statuses =
    [
        ("EN COURS", true),
        ("CLASSES", false),
    ];

    public static ImportPlan Read(string root, CancellationToken cancellationToken = default)
    {
        var candidates = new List<ImportCandidate>();
        var splittable = new List<SplitSuggestion>();
        var skipped = new List<string>();

        foreach (var (folder, isOpen) in Statuses)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
            {
                skipped.Add($"Le dossier « {folder} » est absent.");
                continue;
            }

            foreach (var client in Directory.EnumerateDirectories(path)
                         .OrderBy(Path.GetFileName, StringComparer.CurrentCulture))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileName(client);
                var (files, emails, bytes) = Measure(client, cancellationToken);

                if (files == 0)
                {
                    skipped.Add($"« {name} » ne contient aucun fichier.");
                    continue;
                }

                candidates.Add(new ImportCandidate(
                    client, name, name, isOpen, files, emails, bytes,
                    Directory.EnumerateDirectories(client).Count()));

                if (Affaires(client) is { Count: > 1 } affaires)
                {
                    splittable.Add(new SplitSuggestion(client, name, affaires));
                }
            }
        }

        return new ImportPlan(root, candidates, splittable, skipped);
    }

    /// <summary>
    /// Expands one client folder into a dossier per subfolder. Only ever called because the user asked
    /// for it: <see cref="Read"/> reports the possibility and stops there.
    /// </summary>
    public static IReadOnlyList<ImportCandidate> Split(
        ImportCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<ImportCandidate>();

        foreach (var affaire in Directory.EnumerateDirectories(candidate.SourcePath)
                     .OrderBy(Path.GetFileName, StringComparer.CurrentCulture))
        {
            var (files, emails, bytes) = Measure(affaire, cancellationToken);
            if (files == 0)
            {
                continue;
            }

            parts.Add(candidate with
            {
                SourcePath = affaire,
                Name = Path.GetFileName(affaire) ?? candidate.Name,
                Files = files,
                Emails = emails,
                Bytes = bytes,
                Subfolders = 0,
            });
        }

        // Files sitting directly in the client folder belong to no affaire, and a split would drop
        // them silently. They stay together under the client's own name instead.
        var loose = MeasureShallow(candidate.SourcePath);
        if (loose.Files > 0)
        {
            parts.Add(candidate with
            {
                Files = loose.Files, Emails = loose.Emails, Bytes = loose.Bytes, Subfolders = 0,
            });
        }

        return parts;
    }

    /// <summary>
    /// Subfolders that look like separate affaires rather than a filing scheme.
    ///
    /// <para>The signal is deliberately weak, because it only has to be good enough to raise the
    /// question. A numbered prefix, « 01 Courriers », is how one dossier is organised inside; a name
    /// without one, « TMF c SILLAND », is usually a matter of its own. Loose files at the top mean the
    /// client folder is itself the dossier, so nothing is suggested.</para>
    /// </summary>
    private static IReadOnlyList<string>? Affaires(string client)
    {
        if (Directory.EnumerateFiles(client).Any(file => !IsNoise(file)))
        {
            return null;
        }

        var subfolders = Directory.EnumerateDirectories(client)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        return subfolders.Count > 1 && subfolders.TrueForAll(name => !StartsWithNumber(name))
            ? subfolders
            : null;
    }

    /// <summary>« 01 Courriers », « 02 Procédure ». A filing scheme numbers its folders; an affaire does not.</summary>
    private static bool StartsWithNumber(string name) =>
        name.Length > 1 && char.IsAsciiDigit(name[0]) && char.IsAsciiDigit(name[1]);

    private static (int Files, int Emails, long Bytes) Measure(string path, CancellationToken cancellationToken)
    {
        var files = 0;
        var emails = 0;
        long bytes = 0;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsNoise(file))
            {
                continue;
            }

            files++;
            if (MailFile.LooksLikeMail(file))
            {
                emails++;
            }

            bytes += SizeOf(file);
        }

        return (files, emails, bytes);
    }

    private static (int Files, int Emails, long Bytes) MeasureShallow(string path)
    {
        var files = Directory.EnumerateFiles(path).Where(file => !IsNoise(file)).ToList();

        return (files.Count, files.Count(MailFile.LooksLikeMail), files.Sum(SizeOf));
    }

    private static long SizeOf(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            // A file we cannot stat is still a file we will try to read. Its size is only cosmetic.
            return 0;
        }
    }

    /// <summary>What Windows leaves in a folder and nobody meant to file.</summary>
    private static bool IsNoise(string path)
    {
        var name = Path.GetFileName(path);

        return name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".DS_Store", StringComparison.Ordinal)
            || name.StartsWith("~$", StringComparison.Ordinal);
    }
}
