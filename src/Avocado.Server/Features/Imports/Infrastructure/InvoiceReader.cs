using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Number">« 92 ». What she quotes when a client rings about a facture.</param>
/// <param name="TotalExclVatCents">The figure Avocado records. Everything else is context.</param>
/// <param name="ClientSiren">Nine digits, no spaces. The one field that matches exactly.</param>
/// <param name="Subject">« Honoraires pour assistance juridique dans le cadre du litige LVMH ».</param>
/// <param name="Balances">
/// Whether HT + TVA came to TTC. The invoice states all three, so they can be checked against each
/// other, and a reading that does not add up is a reading to put in front of her rather than import.
/// </param>
public sealed record ReadInvoice(
    string? Number,
    DateOnly? IssuedOn,
    DateOnly? DueOn,
    long? TotalExclVatCents,
    long? VatCents,
    long? TotalInclVatCents,
    bool Paid,
    string? ClientName,
    string? ClientSiren,
    string? Subject,
    bool Balances,
    IReadOnlyList<string> Notes)
{
    /// <summary>Enough to record a facture: what was billed, when, and under which number.</summary>
    public bool IsUsable => TotalExclVatCents is > 0 && IssuedOn is not null;
}

/// <summary>
/// Reads a facture she issued, out of the PDF her invoicing tool produced.
///
/// <para><b>Why rules and not a model.</b> Measured on real invoices from the cabinet's own tool:
/// every one carries a text layer, and the layout is identical from one to the next because one
/// program produced them all. Reading is therefore exact and local, which matters more here than
/// anywhere else in Avocado: these pages carry the client's name, the amount and the cabinet's IBAN,
/// and they are covered by secret professionnel. Nothing about them needs to leave the machine.</para>
///
/// <para><b>What was not assumed.</b> None of the invoices measured were Factur-X, so there is no
/// embedded XML to read; that is worth revisiting when the e-invoicing reform makes it universal,
/// because it would make this class unnecessary for current invoices.</para>
///
/// <para><b>And it checks itself.</b> The invoice states HT, TVA and TTC, so the reading is verified
/// against its own arithmetic. A total read from the wrong line almost never balances, which turns a
/// silent misreading into a row she is asked about.</para>
/// </summary>
public static partial class InvoiceReader
{
    /// <summary>
    /// « Facture N° 92 », and the four other ways invoicing tools write the same thing.
    ///
    /// <para>The degree sign is optional on purpose. It is the character most likely to go missing:
    /// a font whose encoding does not map it yields nothing at all, which is exactly what happened in
    /// this project's own test fixture. Requiring it would make the number unreadable on some tool's
    /// invoices and there would be no way to tell from looking at the page, since it prints fine.
    /// The captured token has to begin with a digit, which is what keeps « Facture Date » out.</para>
    /// </summary>
    [GeneratedRegex(@"Facture\s+(?:n\s*[°º]?\s*)?(\d[A-Za-z0-9\-/]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Number();

    [GeneratedRegex(@"Date\s+d'émission\s*:?\s*(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex Issued();

    [GeneratedRegex(@"Date\s+limite\s+de\s+paiement\s*:?\s*(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex Due();

    [GeneratedRegex(@"SIREN\s*:?\s*(\d[\d\s]{7,})", RegexOptions.IgnoreCase)]
    private static partial Regex Siren();

    /// <summary>
    /// A line whose label is followed by an amount. The € is optional, deliberately: in the totals
    /// block of a real invoice the currency glyph is drawn separately and does not come back with
    /// the number, so requiring it read « Total HT » as having no amount at all.
    /// </summary>
    private static Regex Labelled(string label) =>
        new($@"^{label}\s+(-?\d[\d\s]*(?:[.,]\d{{1,2}})?)\s*[^\d]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ReadInvoice Read(Stream pdf)
    {
        var lines = Lines(pdf, out var rightColumn);
        var notes = new List<string>();

        var whole = string.Join('\n', lines);

        var ht = Amount(lines, "Total\\s+HT");
        var tva = Amount(lines, "TVA");
        var ttc = Amount(lines, "Total");

        // Stated three times over, so it can be checked. A total lifted from the wrong line almost
        // never balances, and one that does is almost certainly the right one.
        var balances = ht is not null && tva is not null && ttc is not null && ht + tva == ttc;

        if (!balances && ht is not null)
        {
            notes.Add("Le total HT, la TVA et le total TTC ne s'accordent pas : à vérifier.");
        }

        var siren = Siren().Matches(rightColumn)
            .Select(match => Digits(match.Groups[1].Value))
            .FirstOrDefault(value => value.Length == 9);

        if (siren is null)
        {
            notes.Add("Le SIREN du client n'a pas été trouvé : le rapprochement se fera sur le nom.");
        }

        return new ReadInvoice(
            Number().Match(whole) is { Success: true } number ? number.Groups[1].Value : null,
            Date(Issued().Match(whole)),
            Date(Due().Match(whole)),
            ht,
            tva,
            ttc,
            // The tool prints « Payée » across the top of an invoice that has been settled.
            whole.Contains("Payée", StringComparison.OrdinalIgnoreCase),
            ClientName(rightColumn),
            siren,
            Subject(lines),
            balances,
            notes);
    }

    /// <summary>
    /// The page as visual lines, plus the right-hand column on its own.
    ///
    /// <para>The columns matter. An invoice carries two SIRENs, the cabinet's and the client's, side
    /// by side, and on a flattened page they land on the same line as each other's neighbours. On the
    /// measured invoices the cabinet sits at x≈18 and the client at x≈492 of a 596-point page, so the
    /// half-way line separates them cleanly and « the other SIREN » never has to be guessed at.</para>
    /// </summary>
    private static List<string> Lines(Stream pdf, out string rightColumn)
    {
        using var document = PdfDocument.Open(pdf);

        var all = new List<string>();
        var right = new List<string>();

        foreach (var page in document.GetPages())
        {
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
            var middle = page.Width / 2;

            foreach (var row in words
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom))
                .OrderByDescending(row => row.Key))
            {
                var ordered = row.OrderBy(word => word.BoundingBox.Left).ToList();

                all.Add(Normalise(string.Join(' ', ordered.Select(word => word.Text))));

                var rightOfMiddle = ordered.Where(word => word.BoundingBox.Left > middle).ToList();

                if (rightOfMiddle.Count > 0)
                {
                    right.Add(Normalise(string.Join(' ', rightOfMiddle.Select(word => word.Text))));
                }
            }
        }

        rightColumn = string.Join('\n', right);
        return all;
    }

    /// <summary>
    /// The last labelled amount on the page, or null.
    ///
    /// <para>The last, because the label appears twice: once as a column heading over the line items,
    /// with no figure beside it, and once in the totals block. Requiring a figure already excludes the
    /// heading; taking the last one survives an invoice that repeats the block per page.</para>
    /// </summary>
    private static long? Amount(IReadOnlyList<string> lines, string label)
    {
        var pattern = Labelled(label);

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (pattern.Match(lines[index]) is { Success: true } match)
            {
                return Cents(match.Groups[1].Value);
            }
        }

        return null;
    }

    /// <summary>« 1 080,00 » and « 600,00 » and « 1080.00 », in cents.</summary>
    public static long? Cents(string written)
    {
        var cleaned = new string(written
            .Where(character => char.IsAsciiDigit(character) || character is ',' or '.' or '-')
            .ToArray())
            .Replace(',', '.');

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (long)Math.Round(value * 100)
            : null;
    }

    private static DateOnly? Date(Match match) =>
        match.Success
        && DateOnly.TryParseExact(match.Groups[1].Value, ["dd/MM/yyyy", "d/M/yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>
    /// The client's name: the first line of the right-hand block that is not a label.
    ///
    /// <para>« N'JOY MOMENTS » sits above its own address, and the address begins with a number. That
    /// is the whole rule, and it is enough because the block is always name then address.</para>
    /// </summary>
    private static string? ClientName(string rightColumn)
    {
        foreach (var line in rightColumn.Split('\n'))
        {
            var candidate = line.Trim();

            if (candidate.Length < 2
                || char.IsAsciiDigit(candidate[0])
                || candidate.StartsWith("SIREN", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Date", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Facture", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Payée", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("N°", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains('@'))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// The line that says what the facture is for, which is the best hint at which dossier it belongs
    /// to: « Litige PHOENIX », « Honoraires … dans le cadre du litige LVMH ».
    ///
    /// <para>It is whatever sits between the addresses and the table heading. Taken as a hint and
    /// never as a decision: the dossier is chosen by her, or matched on the client's SIREN.</para>
    /// </summary>
    private static string? Subject(IReadOnlyList<string> lines)
    {
        var heading = -1;

        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].StartsWith("Détails", StringComparison.OrdinalIgnoreCase))
            {
                heading = index;
                break;
            }
        }

        if (heading <= 0)
        {
            return null;
        }

        for (var index = heading - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();

            if (candidate.Length > 3
                && !candidate.StartsWith("SIREN", StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith("N°", StringComparison.OrdinalIgnoreCase)
                && !candidate.Contains('@'))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Digits(string value) =>
        new([.. value.Where(char.IsAsciiDigit)]);

    /// <summary>
    /// What a PDF's text needs before it can be matched against anything.
    ///
    /// <para>Four things, all of them real in the measured invoices. Word spacing comes back
    /// irregular, so it is collapsed. The typographic apostrophe in « Date d'émission » is not the
    /// one anybody types. The ﬁ ligature is a single character, so « Modiﬁcation » does not contain
    /// « fi » and a search for « identification » finds nothing at all.</para>
    ///
    /// <para>And the fourth, which cost an afternoon: <b>the € in the totals block comes back as
    /// U+0000</b>. The font's encoding does not map that glyph, so PdfPig yields a null character.
    /// A NUL is not whitespace, so it survives Trim, so « Total HT 600,00 » ended in an invisible
    /// character and matched no pattern that expected the line to end after the figure. Every total
    /// on every invoice read as absent. Control characters are therefore dropped outright: in text
    /// lifted from a PDF they never mean anything.</para>
    /// </summary>
    public static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            // See above: the € of the totals block arrives as NUL. Never meaningful, always harmful.
            if (char.IsControl(character))
            {
                continue;
            }

            builder.Append(character switch
            {
                'ﬀ' => "ff",
                'ﬁ' => "fi",
                'ﬂ' => "fl",
                'ﬃ' => "ffi",
                'ﬄ' => "ffl",
                '’' or '‘' or 'ʼ' => "'",
                ' ' or ' ' or ' ' => " ",
                _ => character.ToString(),
            });
        }

        return WhiteSpace().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
