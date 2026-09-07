namespace Avocado.Server.Features.Documents.Folders;

/// <summary>
/// Versement d'une pièce: what number it takes and what the file is called.
///
/// <para>Its own type because a pièce number is cited in conclusions filed with a court. Once
/// communicated it is a reference somebody else relies on, so the rules about which number a file
/// takes are worth stating in one place and testing.</para>
/// </summary>
public static class Exhibits
{
    /// <summary>« Pièces », a real folder in her dossier, readable like anything else in it.</summary>
    public const string Folder = "Pièces";

    /// <summary>
    /// The first number no file claims.
    ///
    /// <para><b>The first free one, not one past the highest.</b> A pièce she withdrew leaves a hole,
    /// and that number may already be cited in conclusions that have been filed; handing it to a
    /// different document would make those conclusions point at the wrong thing. Holes stay open until
    /// she fills them deliberately.</para>
    ///
    /// <para>Read off the folder rather than from a counter in the database, so that renaming or
    /// deleting a pièce in Explorer leaves Avocado agreeing with what is on disk.</para>
    /// </summary>
    public static int NextNumber(IEnumerable<string> fileNames)
    {
        var taken = fileNames
            .Select(NumberOf)
            .OfType<int>()
            .ToHashSet();

        var number = 1;

        while (taken.Contains(number))
        {
            number++;
        }

        return number;
    }

    /// <summary>« Pièce 12 - Attestation.pdf » gives 12. Anything else gives nothing.</summary>
    public static int? NumberOf(string fileName)
    {
        const string prefix = "Pièce ";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = fileName[prefix.Length..].TakeWhile(char.IsAsciiDigit).ToArray();

        return digits.Length > 0 && int.TryParse(digits, out var number) ? number : null;
    }

    /// <summary>« Pièce 12 - Attestation de M. Loubet.pdf ».</summary>
    public static string FileName(int number, string? label, string sourceFileName)
    {
        var written = Safe(label is { Length: > 0 }
            ? label
            : Path.GetFileNameWithoutExtension(sourceFileName));

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
    /// </summary>
    public static string Safe(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var cleaned = new string(label
            .Select(character => invalid.Contains(character) ? ' ' : character)
            .ToArray());

        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Trailing dots and spaces are legal in the string and refused by Windows at creation.
        cleaned = cleaned.TrimEnd('.', ' ');

        return cleaned.Length > 90 ? cleaned[..90].TrimEnd('.', ' ') : cleaned;
    }
}
