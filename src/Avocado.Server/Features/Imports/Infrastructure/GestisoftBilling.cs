using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Process">Gestisoft's own word: « Factures », « Règlements clients ».</param>
/// <param name="Reference">The invoice number, « 202604792 ».</param>
/// <param name="Pointage">
/// What ties a règlement back to the facture it settles. Both rows carry the same value, and it is the
/// only link between them: the amounts repeat across a dossier and the dates do not line up.
/// </param>
/// <param name="DossierCode">« 700978 ». The same number that heads the contacts list.</param>
public sealed record GestisoftBillingRow(
    DateOnly? Date,
    string? Process,
    string? Reference,
    string? Pointage,
    long DebitCents,
    long CreditCents,
    long ExclVatCents,
    long VatCents,
    long BalanceCents,
    string? Label,
    string? Comment,
    string? DossierCode,
    string? DossierLabel)
{
    /// <summary>A facture was issued: the client was debited and a number was given.</summary>
    public bool IsInvoice =>
        DebitCents > 0
        && (Reference is { Length: > 0 } || string.Equals(Process, "Factures", StringComparison.OrdinalIgnoreCase));

    /// <summary>Money came in.</summary>
    public bool IsPayment => CreditCents > 0 && !IsInvoice;
}

/// <summary>
/// Reads export.xlsx, the account Gestisoft prints for one dossier.
///
/// <para>Twenty-three columns of which eight matter, and they are read by name rather than by
/// position: the export is produced by hand from a reporting tool, and there is no reason to believe
/// the next one carries the same column order. A header that is missing leaves its field empty.</para>
///
/// <para>Every amount becomes cents through <see cref="Cents"/>. The file holds binary doubles, so a
/// T.V.A. line reads 266.39999999999998 where 266,40 was meant, and truncating rather than rounding
/// would lose a centime on a fair proportion of the rows.</para>
/// </summary>
public static class GestisoftBilling
{
    public static IReadOnlyList<GestisoftBillingRow> Read(string path)
    {
        try
        {
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.FirstOrDefault();
            var used = sheet?.RangeUsed();

            if (used is null)
            {
                return [];
            }

            var rows = used.RowsUsed().ToList();

            if (rows.Count < 2)
            {
                return [];
            }

            // Which column each header sits in, so the rest of this reads by meaning.
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var cell in rows[0].Cells())
            {
                var key = Normalise(cell.GetString());

                if (key.Length > 0)
                {
                    columns.TryAdd(key, cell.Address.ColumnNumber);
                }
            }

            return rows
                .Skip(1)
                .Select(row => ReadRow(row, columns))
                .OfType<GestisoftBillingRow>()
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or FormatException)
        {
            // A workbook that is not one of these exports, or one Excel still has open. Neither is
            // worth stopping an import of thirteen thousand documents for.
            return [];
        }
    }

    private static GestisoftBillingRow? ReadRow(IXLRangeRow row, Dictionary<string, int> columns)
    {
        var code = Text("codedossier");
        var debit = Money("debit");
        var credit = Money("credit");

        // Gestisoft pads the sheet with blank rows and sometimes repeats the header. Neither carries a
        // dossier number or an amount.
        if (code is null && debit == 0 && credit == 0)
        {
            return null;
        }

        return new GestisoftBillingRow(
            Date(row, columns),
            Text("processus"),
            Text("facture"),
            Text("pointage"),
            debit,
            credit,
            Money("ht"),
            Money("tva"),
            Money("solde"),
            Text("libelle"),
            Text("commentairelong") ?? Text("commentaire"),
            code,
            Text("libelledossier"));

        string? Text(string header)
        {
            if (!columns.TryGetValue(header, out var column))
            {
                return null;
            }

            var cell = row.Cell(column);

            return cell.IsEmpty() || cell.GetFormattedString().Trim() is not { Length: > 0 } value ? null : value;
        }

        long Money(string header) =>
            columns.TryGetValue(header, out var column) && !row.Cell(column).IsEmpty()
                ? Cents(row.Cell(column))
                : 0;
    }

    /// <summary>
    /// The date, whether Excel stored it as a date, as the serial number underneath one, or as text.
    ///
    /// <para>Hers arrive as bare serial numbers, 46080 for the 27th of February 2026, because the
    /// reporting tool writes the value without the format that would make Excel show it as a date. So
    /// a number in that column is read as one, and the range keeps a stray amount out of 1907.</para>
    /// </summary>
    private static DateOnly? Date(IXLRangeRow row, Dictionary<string, int> columns)
    {
        if (!columns.TryGetValue("date", out var column))
        {
            return null;
        }

        var cell = row.Cell(column);

        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var stored))
        {
            return DateOnly.FromDateTime(stored);
        }

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var serial))
        {
            return serial is > 20000 and < 80000
                ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
                : null;
        }

        // The formats spelled out rather than a French culture, because Avocado publishes with
        // InvariantGlobalization and asking for fr-FR there throws rather than falling back.
        var text = cell.GetFormattedString().Trim();

        return DateOnly.TryParseExact(
            text,
            ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
                ? parsed
                : null;
    }

    /// <summary>
    /// An amount in cents, from a cell holding either a double or the text of one.
    ///
    /// <para><see cref="MidpointRounding.AwayFromZero"/>, and that is not a detail: 266.39999999999998
    /// is what the file says for 266,40, and truncation gives 266,39.</para>
    /// </summary>
    private static long Cents(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number))
        {
            return (long)Math.Round(number * 100, MidpointRounding.AwayFromZero);
        }

        var text = cell.GetFormattedString()
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? (long)Math.Round(parsed * 100, MidpointRounding.AwayFromZero)
            : 0;
    }

    /// <summary>
    /// « Débit » and « H.T. » become debit and ht, so a header matches however it is written.
    ///
    /// <para><b>The accents are folded by hand, deliberately.</b> The obvious way is to decompose with
    /// <see cref="NormalizationForm.FormD"/> and drop the combining marks, and it works on a normal
    /// runtime. Avocado publishes with InvariantGlobalization, where Normalize is a no-op that returns
    /// the string unchanged: « Débit » stayed « débit », never matched, and every accented money column
    /// read as zero. Nothing threw and nothing was logged. An import that quietly gives a dossier no
    /// facture at all is the worst failure this module has.</para>
    /// </summary>
    private static string Normalise(string header)
    {
        var builder = new StringBuilder();

        foreach (var character in header)
        {
            var folded = Fold(char.ToLowerInvariant(character));

            if (char.IsAsciiLetter(folded))
            {
                builder.Append(folded);
            }
        }

        return builder.ToString();
    }

    /// <summary>The accented letters French headers actually use, and their plain form.</summary>
    private static char Fold(char character) => character switch
    {
        'à' or 'á' or 'â' or 'ä' or 'ã' or 'å' => 'a',
        'ç' => 'c',
        'è' or 'é' or 'ê' or 'ë' => 'e',
        'ì' or 'í' or 'î' or 'ï' => 'i',
        'ñ' => 'n',
        'ò' or 'ó' or 'ô' or 'ö' or 'õ' => 'o',
        'ù' or 'ú' or 'û' or 'ü' => 'u',
        'ý' or 'ÿ' => 'y',
        _ => character,
    };
}
