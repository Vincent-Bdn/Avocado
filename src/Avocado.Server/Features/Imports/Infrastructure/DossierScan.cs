using Avocado.Server.Features.Mails.Infrastructure;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <summary>
/// Finds the dossiers in a folder tree, wherever they happen to sit.
///
/// <para>The first version expected EN COURS and CLASSES at the top and one folder per client under
/// each. That is not Gestisoft's doing, it is how one practice filed things, and building it into the
/// scanner made the importer useless to anyone who filed differently. This walks whatever it is given
/// and recognises dossiers by their shape.</para>
///
/// <para><b>The shape is numbering.</b> A dossier is the folder whose children are its own filing:
/// « 01 Courriers » appears 37 times in the real export, « 02 Actes » 24, « 04 Réception de pièces »
/// 18. Affaire names, by contrast, are unique, 606 of the 728 distinct folder names in that export
/// occur exactly once. So a folder whose subfolders are numbered, or carry one of the handful of
/// names people use for filing, is a dossier; anything else is a container and is descended into. A
/// folder with no subfolders at all is a flat dossier.</para>
///
/// <para>Depth cannot be the rule, tempting as « last but one » sounds: that tree runs eight levels
/// deep in places and one level in others.</para>
///
/// <para><b>Archived is a property of the path, not of the structure.</b> A dossier filed anywhere
/// under a folder called CLASSES is closed. The words are configurable because they are somebody's
/// filing habit rather than a standard, and getting them wrong should be a setting rather than a
/// rebuild.</para>
/// </summary>
public static class DossierScan
{
    /// <summary>What a folder is called when it holds finished work. Hers says CLASSES.</summary>
    public static readonly string[] DefaultArchivedWords = ["classes", "classés", "classes", "archives", "archivés", "clos", "cloturés", "clôturés"];

    /// <summary>
    /// Folder names that mean « this is how one dossier is organised », rather than naming an affaire.
    /// Numbering carries most of it; these are the ones people write out.
    /// </summary>
    private static readonly HashSet<string> FilingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "emails", "email", "mails", "courriels", "courriers", "gestion", "recherches", "recherche",
        "pieces", "pièces", "facturation", "actes et pièces", "notes et recherches", "notes",
        "pieces client", "pièces client", "docs client", "docs clients", "correspondances",
        "procédure", "procedure", "annexes", "jurisprudence", "contacts et factu", "contact et factu",
    };

    public static ImportPlan Read(
        string root,
        IReadOnlyList<string>? archivedWords = null,
        CancellationToken cancellationToken = default)
    {
        var words = archivedWords is { Count: > 0 } ? archivedWords : DefaultArchivedWords;
        var candidates = new List<ImportCandidate>();
        var skipped = new List<string>();

        Walk(Path.GetFullPath(root), Path.GetFullPath(root), null, 0, words, candidates, skipped, cancellationToken);

        return new ImportPlan(
            root,
            candidates.OrderBy(candidate => candidate.SourcePath, StringComparer.CurrentCulture).ToList(),
            [],
            skipped);
    }

    private static void Walk(
        string folder,
        string root,
        string? client,
        int depth,
        IReadOnlyList<string> archivedWords,
        List<ImportCandidate> candidates,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> subfolders;

        try
        {
            subfolders = Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            skipped.Add($"« {Path.GetFileName(folder)} » n'a pas pu être lu : {exception.Message}");
            return;
        }

        // The root itself is never a dossier, however it is shaped: someone pointing at their whole
        // archive would otherwise import it as one matter containing everything.
        var atRoot = string.Equals(folder, root, StringComparison.OrdinalIgnoreCase);

        if (!atRoot && IsDossier(subfolders))
        {
            var candidate = Describe(folder, root, client, archivedWords, subfolders.Count, cancellationToken);

            if (candidate.Files == 0)
            {
                skipped.Add($"« {candidate.Name} » ne contient aucun fichier.");
            }
            else
            {
                candidates.Add(candidate);
            }

            return;
        }

        // This folder is not a dossier, so it is a level of filing, and which level decides what it
        // means. The root and the section beneath it, EN COURS or CLASSES, name nobody; the level
        // below that is the client. Working it out here, on the way down, is what stops « EN COURS »
        // from becoming a client with thirteen affaires under it.
        var childClient = depth < 2 ? null : client ?? Path.GetFileName(folder);

        foreach (var subfolder in subfolders)
        {
            Walk(subfolder, root, childClient, depth + 1, archivedWords, candidates, skipped, cancellationToken);
        }
    }

    /// <summary>
    /// A folder is a dossier when it has no subfolders, or when any of them is filing.
    ///
    /// <para>Any, rather than most, because a dossier files what it happens to have: « Vivre Greffé »
    /// keeps Docs client, Droit de la santé, Gestion and Modeles données perso, and only two of those
    /// are words anyone would list in advance. Demanding a majority broke it into six matters.</para>
    ///
    /// <para>What makes « any » safe is that a filing name is narrow. The danger is a client folder
    /// that happens to look like one and drags its fifty-eight siblings into a single dossier, which
    /// is exactly what a client called « 2 RIDE » did while one leading digit counted as numbering.
    /// Two digits is the answer: drawers are numbered 01, 02, 03, and clients are not.</para>
    /// </summary>
    private static bool IsDossier(List<string> subfolders) =>
        subfolders.Count == 0
        || subfolders.Select(Path.GetFileName).OfType<string>().Any(IsFilingFolder);

    /// <summary>
    /// « 01 Courriers », « 4. Pièces », « Emails ».
    ///
    /// <para>A name that is nothing but digits is deliberately excluded, and it matters: « 700770 » and
    /// « 149860 » are Gestisoft's numbers for the dossiers themselves. Counting them as filing made
    /// COULEYRE the dossier and its own matter number a drawer inside it.</para>
    /// </summary>
    private static bool IsFilingFolder(string name)
    {
        var trimmed = name.Trim();

        if (trimmed.Length == 0 || trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        // Two leading digits, then a separator. Two and not one, because « 01 Courriers » and
        // « 02 Actes » are how a drawer is numbered while « 2 RIDE » is the name of a client.
        var digits = trimmed.TakeWhile(char.IsAsciiDigit).Count();

        return (digits == 2 && trimmed.Length > 2 && trimmed[2] is ' ' or '-' or '_' or '.')
            || FilingNames.Contains(trimmed);
    }

    private static ImportCandidate Describe(
        string folder,
        string root,
        string? client,
        IReadOnlyList<string> archivedWords,
        int subfolders,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(folder) ?? folder;
        var relative = Path.GetRelativePath(root, folder).Split(Path.DirectorySeparatorChar);

        // Archived is decided by the whole path, so « CLASSES » anywhere above it closes the dossier
        // wherever it sits in the tree.
        var archived = relative.Any(segment => archivedWords.Any(word =>
            segment.Trim().Equals(word, StringComparison.OrdinalIgnoreCase)));

        // A dossier nobody grouped under a client stands for its own: forty of hers are a client
        // folder with the files straight inside it.
        client ??= name;

        var (files, emails, bytes, contacts, billing) = Measure(folder, cancellationToken);

        return new ImportCandidate(
            folder, client, name, !archived, files, emails, bytes, subfolders,
            GestisoftCode(name), contacts, billing);
    }

    /// <summary>
    /// Counts what is inside, and picks up the two files Gestisoft can export beside a dossier.
    ///
    /// <para>Found by what they are rather than by where they sit: she filed them under « Contacts et
    /// factu » and « Contact et factu », and named them contacts.PDF, contact.PDF and
    /// « Contacts couleyre.PDF ». Matching folder names would have found five of seven. A PDF whose
    /// name mentions contacts, and a spreadsheet, are the things themselves.</para>
    /// </summary>
    private static (int Files, int Emails, long Bytes, string? Contacts, string? Billing) Measure(
        string folder,
        CancellationToken cancellationToken)
    {
        var files = 0;
        var emails = 0;
        long bytes = 0;
        string? contacts = null;
        string? billing = null;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);
            if (IsNoise(name))
            {
                continue;
            }

            files++;

            if (MailFile.LooksLikeMail(file))
            {
                emails++;
            }

            var lower = name.ToLowerInvariant();

            if (contacts is null && lower.EndsWith(".pdf", StringComparison.Ordinal) && lower.Contains("contact", StringComparison.Ordinal))
            {
                contacts = file;
            }

            if (billing is null && lower.EndsWith(".xlsx", StringComparison.Ordinal))
            {
                billing = file;
            }

            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return (files, emails, bytes, contacts, billing);
    }

    /// <summary>
    /// « 700770 », « 149860 ». Gestisoft's own number for the dossier, when the folder is named after
    /// it. It is also the heading of the contacts list and a column in the billing export, which makes
    /// it the one identifier all three sources share.
    /// </summary>
    private static string? GestisoftCode(string name)
    {
        var trimmed = name.Trim();

        return trimmed.Length is >= 5 and <= 8 && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }

    private static bool IsNoise(string name) =>
        name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".DS_Store", StringComparison.Ordinal)
        || name.StartsWith("~$", StringComparison.Ordinal);
}
