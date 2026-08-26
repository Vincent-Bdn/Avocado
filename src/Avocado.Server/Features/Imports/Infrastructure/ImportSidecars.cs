using System.Globalization;
using System.Text;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Dossier">The client folder's name, which is how a row finds its dossier.</param>
public sealed record TiersRow(string Dossier, string Nom, string Type, string Role, string Email, string Telephone, string Adresse);

/// <param name="Type">« facture », « encaissement » or « débours ». Free text, matched loosely.</param>
public sealed record FacturationRow(string Dossier, DateOnly Date, string Type, long AmountCents, string Libelle, string Reference, bool Paye);

/// <param name="Problems">Lines that could not be read, named so they can be corrected rather than guessed at.</param>
public sealed record Sidecars(
    IReadOnlyList<TiersRow> Tiers,
    IReadOnlyList<FacturationRow> Facturation,
    IReadOnlyList<string> Problems);

/// <summary>
/// The two things the Gestisoft export does not contain, supplied by hand.
///
/// <para>There is no contacts file and no billing in the export, so they can only come from her. A
/// spreadsheet is the honest medium for that: she already has Excel, she already knows it, and a
/// hundred dossiers of parties is data entry whatever tool it happens in. Avocado writes the templates
/// with the dossier names already filled so the only thing left to type is what it does not know.</para>
///
/// <para>Semicolons, not commas, and UTF-8 with a byte order mark. That is what a French Excel writes
/// and reads without asking anything, and a template that opens as one column of gibberish is a
/// template nobody fills in.</para>
///
/// <para>Nothing here refuses a file. A row that cannot be read is reported by name and the rest are
/// kept: an import of eighty dossiers must not fail because line 41 has a comma in an amount.</para>
/// </summary>
public static class ImportSidecars
{
    public const string TiersFileName = "avocado-tiers.csv";
    public const string FacturationFileName = "avocado-facturation.csv";

    private static readonly UTF8Encoding ExcelFriendly = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Writes both templates, one row per dossier, so she fills columns rather than inventing a
    /// format. Existing files are never overwritten: the second run would erase an afternoon of typing.
    /// </summary>
    public static IReadOnlyList<string> WriteTemplates(string folder, IReadOnlyList<ImportCandidate> candidates)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();

        var tiers = Path.Combine(folder, TiersFileName);
        if (!File.Exists(tiers))
        {
            var lines = new List<string> { "dossier;nom;type;role;email;telephone;adresse" };
            lines.AddRange(candidates.Select(candidate =>
                $"{Escape(candidate.Name)};;;;;;"));

            File.WriteAllLines(tiers, lines, ExcelFriendly);
            written.Add(tiers);
        }

        var facturation = Path.Combine(folder, FacturationFileName);
        if (!File.Exists(facturation))
        {
            var lines = new List<string> { "dossier;date;type;montant_ht;libelle;reference;paye" };
            lines.AddRange(candidates.Select(candidate =>
                $"{Escape(candidate.Name)};;;;;;"));

            File.WriteAllLines(facturation, lines, ExcelFriendly);
            written.Add(facturation);
        }

        return written;
    }

    /// <summary>Reads whichever of the two exist beside the export. Both are optional.</summary>
    public static Sidecars Read(string folder)
    {
        var problems = new List<string>();

        return new Sidecars(
            ReadTiers(Path.Combine(folder, TiersFileName), problems),
            ReadFacturation(Path.Combine(folder, FacturationFileName), problems),
            problems);
    }

    private static IReadOnlyList<TiersRow> ReadTiers(string path, List<string> problems)
    {
        var rows = new List<TiersRow>();

        foreach (var (fields, line) in Rows(path, problems))
        {
            var dossier = Field(fields, 0);
            var nom = Field(fields, 1);

            // A template row nobody filled in. Not a problem, just nothing to do.
            if (dossier.Length == 0 || nom.Length == 0)
            {
                continue;
            }

            rows.Add(new TiersRow(
                dossier, nom, Field(fields, 2), Field(fields, 3),
                Field(fields, 4), Field(fields, 5), Field(fields, 6)));

            _ = line;
        }

        return rows;
    }

    private static IReadOnlyList<FacturationRow> ReadFacturation(string path, List<string> problems)
    {
        var rows = new List<FacturationRow>();
        var number = 1;

        foreach (var (fields, _) in Rows(path, problems))
        {
            number++;

            var dossier = Field(fields, 0);
            var amount = Field(fields, 3);

            if (dossier.Length == 0 && amount.Length == 0)
            {
                continue;
            }

            if (ParseDate(Field(fields, 1)) is not { } date)
            {
                problems.Add($"{Path.GetFileName(path)}, ligne {number} : date illisible « {Field(fields, 1)} ».");
                continue;
            }

            if (ParseAmount(amount) is not { } cents)
            {
                problems.Add($"{Path.GetFileName(path)}, ligne {number} : montant illisible « {amount} ».");
                continue;
            }

            rows.Add(new FacturationRow(
                dossier, date, Field(fields, 2), cents,
                Field(fields, 4), Field(fields, 5),
                Field(fields, 6) is "oui" or "OUI" or "1" or "x" or "X" or "vrai"));
        }

        return rows;
    }

    private static IEnumerable<(string[] Fields, string Line)> Rows(string path, List<string> problems)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (IOException exception)
        {
            problems.Add($"{Path.GetFileName(path)} : {exception.Message}");
            yield break;
        }

        // The first line is the header she was given. Skipping it by position rather than by matching
        // its text, since renaming a column in Excel is easy and should not silently drop a row.
        foreach (var line in lines.Skip(1))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            yield return (Split(line), line);
        }
    }

    /// <summary>
    /// Semicolon separated, with quoted fields so a libellé may contain one. Not a full CSV parser and
    /// does not need to be: this reads a template Avocado wrote, filled in by one person in Excel.
    /// </summary>
    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                // Excel doubles a quote inside a quoted field.
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ';' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return [.. fields];
    }

    private static string Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : string.Empty;

    /// <summary>
    /// French first, because that is what she will type and what her Excel writes. ISO is accepted
    /// too, since a spreadsheet formatted as a date sometimes comes out that way.
    /// </summary>
    private static DateOnly? ParseDate(string value)
    {
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];

        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    /// « 1 234,56 », « 1234.56 », « 1 234,56 € ». Everything a French spreadsheet produces, including
    /// the non-breaking space Excel uses as a thousands separator, which is not the space anyone types.
    /// </summary>
    private static long? ParseAmount(string value)
    {
        var cleaned = new StringBuilder();

        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                cleaned.Append(character);
            }
            else if (character is ',' or '.')
            {
                cleaned.Append('.');
            }
            else if (character == '-' && cleaned.Length == 0)
            {
                cleaned.Append('-');
            }
        }

        var text = cleaned.ToString();

        // A number with two separators is « 1.234,56 »: the first is thousands, the last is decimals.
        var lastStop = text.LastIndexOf('.');
        if (text.Count(c => c == '.') > 1 && lastStop >= 0)
        {
            text = text[..lastStop].Replace(".", string.Empty) + text[lastStop..];
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? (long)Math.Round(amount * 100)
            : null;
    }

    private static string Escape(string value) =>
        value.Contains(';') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
