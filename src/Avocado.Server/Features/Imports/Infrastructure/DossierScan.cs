using Avocado.Server.Features.Mails.Infrastructure;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <summary>
/// Reads a folder tree so that somebody can say which folders are dossiers.
///
/// <para><b>It used to decide, and deciding is what went wrong.</b> The rule was a good one, a dossier
/// is the folder whose children are its own filing, and it is right for most of a real export. Where
/// it is wrong it is wrong invisibly. CHANTERACOISE keeps CA, MED and Tcom, none of which reads as
/// filing, so the scan walked past it and offered « Assignation et nos conclusions » and
/// « Conclusions adv » as two separate affaires with three and two documents in them. Nothing on the
/// screen said which client they came from, and there was no way to say « no, the dossier is
/// CHANTERACOISE ». Worse: the six files sitting loose in CHANTERACOISE itself belonged to no
/// candidate at all and would have been dropped in silence, 81 files across the real export.</para>
///
/// <para>So this returns the tree with its counts, and marks what it would have chosen. The choice
/// belongs to the person who knows what the affaires were. Marking a folder takes everything below it,
/// which is also what replaced the old split-a-client control: choosing the children instead of the
/// parent <em>is</em> the split.</para>
///
/// <para><b>Archived is a property of the path.</b> A folder anywhere under one called CLASSES holds
/// finished work. The words are configurable because they are somebody's filing habit rather than a
/// standard, and getting them wrong should be a setting rather than a rebuild.</para>
/// </summary>
public static class DossierScan
{
    /// <summary>What a folder is called when it holds finished work. Hers says CLASSES.</summary>
    public static readonly string[] DefaultArchivedWords =
        ["classes", "classés", "archives", "archivés", "clos", "cloturés", "clôturés"];

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
        var full = Path.GetFullPath(root);
        var skipped = new List<string>();

        var tree = Walk(full, full, null, 0, words, skipped, cancellationToken);

        // The folder she pointed at is a row like any other, and markable like any other. It used to be
        // held back, on the grounds that nobody means to import their whole archive as one matter, and
        // that was right while the scan decided on its own. Now that she marks them, holding it back
        // only hid things: files lying loose in the folder she chose were counted nowhere and imported
        // nowhere, and pointing at a single dossier to import just that one was impossible.
        return new ImportPlan(root, tree is null ? [] : [Project(tree)], skipped);
    }

    /// <summary>
    /// The dossiers to import: the folders she marked, or what the scan suggested if she marked
    /// nothing.
    ///
    /// <para>Pure, because the tree already carries every number an import needs. Nothing is read from
    /// disk twice, and what she saw on screen is exactly what runs.</para>
    ///
    /// <para>A dossier inside a dossier is dropped rather than refused. Marking a parent takes
    /// everything below it, so a child left marked would import its files a second time, and a
    /// duplicate is the kind of thing found a year later.</para>
    /// </summary>
    public static IReadOnlyList<ImportCandidate> Candidates(
        ImportPlan plan,
        IReadOnlyList<string>? chosen = null)
    {
        // Null is « she has not chosen », an empty list is « she chose nothing ». Not the same thing,
        // and conflating them would run a whole import she had just emptied.
        var marks = chosen is null ? null : new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);

        var candidates = new List<ImportCandidate>();

        void Gather(ImportFolder folder)
        {
            if (marks is null ? folder.Suggested : marks.Contains(folder.Path))
            {
                if (folder.TotalFiles > 0)
                {
                    candidates.Add(new ImportCandidate(
                        folder.Path,
                        folder.Client,
                        folder.Name,
                        folder.IsOpen,
                        folder.TotalFiles,
                        folder.TotalEmails,
                        folder.TotalBytes,
                        folder.Children.Count,
                        folder.GestisoftCode,
                        folder.ContactsFile,
                        folder.BillingFile));
                }

                // Everything below belongs to this one now.
                return;
            }

            foreach (var child in folder.Children)
            {
                Gather(child);
            }
        }

        foreach (var folder in plan.Folders)
        {
            Gather(folder);
        }

        return candidates;
    }

    /// <summary>Files under the root that no marked folder would take. Zero is the goal, not a given.</summary>
    public static int Orphans(ImportPlan plan, IReadOnlyList<ImportCandidate> candidates) =>
        plan.Files - candidates.Sum(candidate => candidate.Files);

    private sealed class Node
    {
        public required string Path;
        public required string Name;
        public required string Client;
        public required bool IsOpen;
        public required bool Suggested;
        public int Files;
        public int Emails;
        public long Bytes;
        public int TotalFiles;
        public int TotalEmails;
        public long TotalBytes;
        public (string Path, int Score)? Contacts;
        public (string Path, int Score)? Billing;
        public List<Node> Children = [];
    }

    private static Node? Walk(
        string folder,
        string root,
        string? client,
        int depth,
        IReadOnlyList<string> archivedWords,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> subfolders;

        try
        {
            subfolders = Directory.EnumerateDirectories(folder)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCulture)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            skipped.Add($"« {Path.GetFileName(folder)} » n'a pas pu être lu : {exception.Message}");
            return null;
        }

        var name = Path.GetFileName(folder) is { Length: > 0 } own ? own : folder;

        // The root and the section beneath it, EN COURS or CLASSES, name nobody; the level below that
        // is the client. Working it out on the way down is what stops « EN COURS » from becoming a
        // client with thirteen affaires under it.
        var childClient = depth < 2 ? null : client ?? name;

        var node = new Node
        {
            Path = folder,
            Name = name,
            Client = client ?? name,
            IsOpen = !IsArchived(folder, root, archivedWords),
            Suggested = depth > 0 && IsDossier(subfolders),
        };

        foreach (var subfolder in subfolders)
        {
            if (Walk(subfolder, root, childClient, depth + 1, archivedWords, skipped, cancellationToken)
                is { } child)
            {
                node.Children.Add(child);
            }
        }

        Measure(node, cancellationToken);

        node.TotalFiles = node.Files + node.Children.Sum(child => child.TotalFiles);
        node.TotalEmails = node.Emails + node.Children.Sum(child => child.TotalEmails);
        node.TotalBytes = node.Bytes + node.Children.Sum(child => child.TotalBytes);

        // The best sidecar anywhere below, carried up one level at a time. She files them in a folder
        // beside the dossier, so the dossier is where they have to arrive.
        foreach (var child in node.Children)
        {
            if (Better(child.Contacts, node.Contacts)) node.Contacts = child.Contacts;
            if (Better(child.Billing, node.Billing)) node.Billing = child.Billing;
        }

        // A folder with nothing in it and nothing below is not a row worth showing. She has hundreds.
        return node.TotalFiles == 0 && depth > 0 ? null : node;
    }

    private static bool Better((string Path, int Score)? candidate, (string Path, int Score)? standing) =>
        candidate is { } offered && offered.Score > (standing?.Score ?? 0);

    /// <summary>
    /// A folder is what the scan would call a dossier when it has no subfolders, or when any of them
    /// is filing.
    ///
    /// <para>Any, rather than most, because a dossier files what it happens to have: « Vivre Greffé »
    /// keeps Docs client, Droit de la santé, Gestion and Modeles données perso, and only two of those
    /// are words anyone would list in advance. Demanding a majority broke it into six matters.</para>
    ///
    /// <para>What makes « any » safe is that a filing name is narrow. The danger is a client folder
    /// that happens to look like one and drags its fifty-eight siblings into a single dossier, which
    /// is exactly what a client called « 2 RIDE » did while one leading digit counted as numbering.</para>
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

    private static bool IsArchived(string folder, string root, IReadOnlyList<string> archivedWords) =>
        Path.GetRelativePath(root, folder)
            .Split(Path.DirectorySeparatorChar)
            .Any(segment => archivedWords.Any(word =>
                segment.Trim().Equals(word, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// What sits directly in this folder, and the two files Gestisoft can export beside a dossier.
    ///
    /// <para>Directly, and not recursively, because every folder is measured exactly once and the
    /// totals are summed on the way back up. Measuring each node's whole subtree would read the real
    /// export's 13 937 files once per level of nesting, seven deep in places.</para>
    ///
    /// <para>Both sidecars are scored rather than matched. Taking any PDF whose name mentions contacts
    /// chose, in COULEYRE, a letter called « pas de contact connu chez EDF OA » over the real list two
    /// folders away, and taking any spreadsheet claimed fourteen billing exports where seven exist.
    /// What tells the real ones apart is how the name begins and whether the folder is about contacts
    /// or facturation.</para>
    /// </summary>
    private static void Measure(Node node, CancellationToken cancellationToken)
    {
        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(node.Path).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var beside = IsSidecarFolder(node.Name);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);

            if (IsNoise(name))
            {
                continue;
            }

            node.Files++;

            if (MailFile.LooksLikeMail(file))
            {
                node.Emails++;
            }

            var lower = name.ToLowerInvariant();

            if (lower.EndsWith(".pdf", StringComparison.Ordinal))
            {
                var score =
                    (lower.StartsWith("contact", StringComparison.Ordinal) ? 3 : 0)
                    + (beside ? 2 : 0)
                    + (lower.Contains("contact", StringComparison.Ordinal) ? 1 : 0);

                if (score >= 3 && score > (node.Contacts?.Score ?? 0))
                {
                    node.Contacts = (file, score);
                }
            }

            if (lower.EndsWith(".xlsx", StringComparison.Ordinal) || lower.EndsWith(".xls", StringComparison.Ordinal))
            {
                var score =
                    (lower.StartsWith("export", StringComparison.Ordinal) ? 3 : 0)
                    + (beside ? 2 : 0)
                    + (lower.Contains("factu", StringComparison.Ordinal) ? 1 : 0);

                if (score >= 3 && score > (node.Billing?.Score ?? 0))
                {
                    node.Billing = (file, score);
                }
            }

            try
            {
                node.Bytes += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Whether this is the folder she keeps the Gestisoft exports in. She wrote « Contacts et factu »
    /// in some dossiers and « Contact et factu » in others, so this asks what the name is about rather
    /// than what it says.
    /// </summary>
    private static bool IsSidecarFolder(string name)
    {
        var lower = name.ToLowerInvariant();

        return lower.Contains("contact", StringComparison.Ordinal)
            || lower.Contains("factu", StringComparison.Ordinal);
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

    private static ImportFolder Project(Node node) => new(
        node.Path,
        node.Name,
        node.Client,
        node.IsOpen,
        node.Files,
        node.Emails,
        node.TotalFiles,
        node.TotalEmails,
        node.TotalBytes,
        node.Suggested,
        GestisoftCode(node.Name),
        node.Contacts?.Path,
        node.Billing?.Path,
        node.Children.Select(Project).ToList());
}
