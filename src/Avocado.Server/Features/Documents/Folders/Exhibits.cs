namespace Avocado.Server.Features.Documents.Folders;

/// <summary>
/// Un numéro de pièce: « 12 », « 68 bis », « 68-1 ».
///
/// <para><b>Why a pièce number is not an int.</b> « Option d'ajouter bis, ex. Pièce 68 bis, ou 68-1
/// 68-2. » A pièce is communicated and then cited by its number in conclusions filed with a court;
/// when something arrives after the bordereau was sent, it cannot take n° 69 — that one is already
/// cited as something else — and it cannot take 68 either. It takes 68 bis. The suffix is the only
/// way to add a pièce to a sequence that has already left the building.</para>
///
/// <para><b>The suffix is never generated.</b> She writes it, because « bis » means something: this
/// document belongs with 68. Avocado numbering it automatically would be inventing that claim.</para>
/// </summary>
/// <param name="Number">The integer part, which is what orders pièces and what the counter advances.</param>
/// <param name="Suffix">« bis », « ter », « 1 ». Null for an ordinary pièce.</param>
public readonly record struct ExhibitNumber(int Number, string? Suffix)
{
    public ExhibitNumber(int number) : this(number, null)
    {
    }

    public bool HasSuffix => Suffix is { Length: > 0 };

    /// <summary>
    /// « 68 bis » for a word, « 68-1 » for a figure.
    ///
    /// <para>Both spellings are hers and both are in the request. The rule is the one that reads
    /// correctly in each case: a word wants a space, a figure wants a hyphen, because « 68 1 » is
    /// two numbers and « 68-bis » is not how anyone writes it.</para>
    /// </summary>
    public override string ToString() => Suffix switch
    {
        null or "" => Number.ToString(),
        var suffix when suffix.All(char.IsAsciiDigit) => $"{Number}-{suffix}",
        var suffix => $"{Number} {suffix}",
    };

    /// <summary>The next ordinary pièce. A suffix is never carried forward; it belonged to one document.</summary>
    public ExhibitNumber Next() => new(Number + 1);
}

/// <summary>
/// Versement d'une pièce: what number it takes and what the file is called.
///
/// <para>Its own type because a pièce number is cited in conclusions filed with a court. Once
/// communicated it is a reference somebody else relies on, so the rules about which number a file
/// takes are worth stating in one place and testing.</para>
/// </summary>
public static class Exhibits
{
    /// <summary>
    /// Where pièces are written, a real folder in her dossier, readable like anything else in it.
    ///
    /// <para>« Quand ça crée un onglet pièce dans mon folder DOSSIER EN COURS / Dossier X, plutôt que
    /// Pièces, Pièces Avocado. » A cabinet's dossier folder already has a « Pièces » in it, kept by
    /// hand for years, and a second thing writing into it is both a surprise and a merge nobody
    /// asked for. Avocado's own folder says whose it is.</para>
    /// </summary>
    public const string Folder = "Pièces Avocado";

    /// <summary>
    /// The name Avocado used before, still read and never written.
    ///
    /// <para>Every pièce versed until now is in there, and the numbers are cited in documents that
    /// have been filed. Ignoring it would restart the counter at 1 and write « Pièce 1 » beside a
    /// « Pièce 1 » from last year; renaming it would break a path somebody's conclusions point at.
    /// So both are read, one is written, and her folder is left exactly where she put it.</para>
    /// </summary>
    public const string LegacyFolder = "Pièces";

    /// <summary>Both, newest name first. What every « which numbers are taken » question reads.</summary>
    public static readonly string[] Folders = [Folder, LegacyFolder];

    /// <summary>
    /// The names of every pièce under this dossier, from both folders.
    ///
    /// <para>The one place in this type that touches a disk, and it is here rather than in the
    /// endpoints because « which files are pièces of this dossier » is exhibit knowledge, and three
    /// callers had started answering it each in their own way.</para>
    /// </summary>
    public static IReadOnlyList<string> FileNamesIn(string root)
    {
        var names = new List<string>();

        foreach (var folder in Folders)
        {
            var path = Path.Combine(root, folder);

            if (!Directory.Exists(path))
            {
                continue;
            }

            names.AddRange(Directory.EnumerateFiles(path).Select(Path.GetFileName).OfType<string>());
        }

        return names;
    }

    /// <summary>
    /// One past the highest, and never a number that has been used and freed.
    ///
    /// <para><b>A hole is not an opening.</b> Withdrawing a pièce leaves its number unclaimed, and that
    /// number may already be cited in conclusions filed with a court. Handing it to a different
    /// document on the next versement would make those conclusions point at something else, silently,
    /// weeks later. So the counter only ever goes up.</para>
    ///
    /// <para>The freed ones are offered back through <see cref="FreeNumbers"/>, to be reused
    /// deliberately or not at all. Which is what the rest of the application already said in words:
    /// « ils restent libres volontairement, ces numéros pouvant être cités dans des conclusions déjà
    /// déposées ».</para>
    ///
    /// <para>Read off the folder rather than from a counter in the database, so that renaming or
    /// deleting a pièce in Explorer leaves Avocado agreeing with what is on disk.</para>
    ///
    /// <para>A « 68 bis » claims 68 as firmly as a plain « Pièce 68 » does: it exists because 68 did,
    /// and handing 68 to something else afterwards is the mistake this whole type exists to prevent.</para>
    /// </summary>
    public static int NextNumber(IEnumerable<string> fileNames)
    {
        var taken = Integers(fileNames);

        return taken.Count == 0 ? 1 : taken.Max() + 1;
    }

    /// <summary>
    /// Numbers below the highest that no file claims: withdrawn pièces, offered for deliberate reuse.
    /// </summary>
    public static IReadOnlyList<int> FreeNumbers(IEnumerable<string> fileNames)
    {
        var taken = Integers(fileNames);

        return taken.Count == 0
            ? []
            : [.. Enumerable.Range(1, taken.Max()).Where(number => !taken.Contains(number))];
    }

    /// <summary>
    /// Whether this exact number, suffix included, is already on a file.
    ///
    /// <para>The pair, not the integer: « 68 bis » is free on a dossier that has « 68 », which is the
    /// entire point of it, and it is taken once something carries it.</para>
    /// </summary>
    public static bool IsTaken(IEnumerable<string> fileNames, ExhibitNumber number) =>
        fileNames.Select(NumberOf).OfType<ExhibitNumber>().Contains(number);

    private static HashSet<int> Integers(IEnumerable<string> fileNames) =>
        [.. fileNames.Select(NumberOf).OfType<ExhibitNumber>().Select(number => number.Number)];

    /// <summary>
    /// « Pièce 12 - Attestation.pdf » gives 12, « Pièce 68 bis - … » gives 68 bis, « Pièce 68-1 - … »
    /// gives 68-1. Anything else gives nothing.
    /// </summary>
    public static ExhibitNumber? NumberOf(string fileName)
    {
        const string prefix = "Pièce ";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = fileName.AsSpan()[prefix.Length..];
        var digits = 0;

        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(rest[..digits], out var number))
        {
            return null;
        }

        return new ExhibitNumber(number, SuffixIn(rest[digits..]));
    }

    /// <summary>The longest a « bis » or a « quater » gets. Past this it is a libellé.</summary>
    private const int LongestSuffix = 6;

    /// <summary>
    /// The suffix in what follows the digits, or null.
    ///
    /// <para>The separator tells it apart from the libellé: « Pièce 68 - Attestation.pdf » has
    /// « - » surrounded by spaces, which is the name separator and never a suffix, while « 68 bis »
    /// and « 68-1 » have no space on both sides of theirs. Reading the libellé as a suffix would make
    /// every pièce ever versed a suffixed one.</para>
    ///
    /// <para><b>And the separator alone is not enough.</b> Plenty of files are named by hand without
    /// one — « Pièce 3 Contrat de bail.pdf » — and those are now read, because Avocado reads the
    /// « Pièces » folder a cabinet kept itself. That name gave <c>ExhibitNumber(3, "Contrat")</c> and
    /// displayed as « pièce 3 Contrat ». So what follows the suffix has to be the end of the stem,
    /// the extension, or the libellé separator: a word with more words after it is a title, and a
    /// suffix is short.</para>
    /// </summary>
    private static string? SuffixIn(ReadOnlySpan<char> after)
    {
        if (after.StartsWith(" - ") || after.Length < 2)
        {
            return null;
        }

        if (after[0] is not (' ' or '-'))
        {
            return null;
        }

        var token = after[1..];
        var length = 0;

        while (length < token.Length && char.IsLetterOrDigit(token[length]))
        {
            length++;
        }

        if (length is 0 or > LongestSuffix)
        {
            return null;
        }

        var rest = token[length..];

        var ends = rest.IsEmpty
            || rest.StartsWith(" - ")
            || (rest[0] == '.' && !rest.Contains(' '));

        return ends ? token[..length].ToString() : null;
    }

    /// <summary>
    /// « Pièce 12 - Attestation de M. Loubet.pdf » gives « Attestation de M. Loubet ».
    ///
    /// <para>What the bordereau lists. The libellé is written for a magistrate and the extension is
    /// not part of it, so both the number and the suffix come off.</para>
    /// </summary>
    public static string? LabelOf(string fileName)
    {
        if (NumberOf(fileName) is null)
        {
            return null;
        }

        var separator = fileName.IndexOf(" - ", StringComparison.Ordinal);

        if (separator < 0)
        {
            return null;
        }

        var label = Path.GetFileNameWithoutExtension(fileName[(separator + 3)..]).Trim();

        return label.Length == 0 ? null : label;
    }

    /// <summary>
    /// « Pièce 12 - Attestation de M. Loubet.pdf », « Pièce 68 bis - Avenant.pdf ».
    ///
    /// <para>With no libellé the source file's own name is used, and a file already called « Pièce 1
    /// - Photos.pdf » produced « Pièce 1 - Pièce 1 - Photos.pdf ». It happens whenever she verses
    /// something she had numbered by hand, or verses the same file twice, which is often enough that
    /// one reached a greffe. <see cref="LabelOf"/> already knows how to take the prefix off.</para>
    /// </summary>
    public static string FileName(ExhibitNumber number, string? label, string sourceFileName)
    {
        var written = Safe(label is { Length: > 0 }
            ? label
            : LabelOf(sourceFileName) ?? Path.GetFileNameWithoutExtension(sourceFileName));

        var extension = Path.GetExtension(sourceFileName);

        return written.Length == 0
            ? $"Pièce {number}{extension}"
            : $"Pièce {number} - {written}{extension}";
    }

    /// <summary>
    /// A libellé is a sentence and a file name is not.
    ///
    /// <para>« Contrat de maintenance du 3/03/2019 : avenants » is a perfectly good libellé and three
    /// of those characters cannot appear in a Windows file name. They become spaces rather than being
    /// dropped, so words do not run together, and the whole is capped: the folder is somewhere inside
    /// her Documents already, and a path Windows refuses to open would be worse than a shortened
    /// name.</para>
    ///
    /// <para>Refused everywhere, not merely here. See <see cref="PortableFileName"/>: on a Mac the
    /// colon in that libellé is legal, and the pièce would then be a file nobody can open the day the
    /// dossier reaches a Windows machine.</para>
    /// </summary>
    public static string Safe(string label)
    {
        var cleaned = PortableFileName.Clean(label, ' ');

        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Trailing dots and spaces are legal in the string and refused by Windows at creation.
        cleaned = cleaned.TrimEnd('.', ' ');

        return cleaned.Length > 90 ? cleaned[..90].TrimEnd('.', ' ') : cleaned;
    }

    /// <summary>
    /// A suffix as it can go in a file name, or null.
    ///
    /// <para>Letters and digits only, because this lands between the number and the libellé separator
    /// and a « - » or a space in it would make the name unparseable by <see cref="NumberOf"/> — which
    /// is to say it would make the pièce's own number unreadable.</para>
    /// </summary>
    public static string? CleanSuffix(string? suffix)
    {
        if (suffix is null)
        {
            return null;
        }

        var kept = new string([.. suffix.Where(char.IsLetterOrDigit)]);

        return kept.Length == 0 ? null : kept.ToLowerInvariant();
    }
}
