using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Avocado.Server.Features.Billings.Infrastructure;

/// <param name="Date">The day the diligence was carried out.</param>
/// <param name="Minutes">Its duration. Rendered in decimal hours, as the template does.</param>
/// <param name="Task">The observation, in her words.</param>
/// <param name="RateCents">What that line is charged at, its dérogation included.</param>
public sealed record DetailLine(DateOnly Date, int Minutes, string Task, long RateCents);

/// <summary>
/// « DÉTAIL FACTURE N° 90 »: the annexe that goes to the client with the facture, as a Word document.
///
/// <para><b>Why Word and not the spreadsheet this replaced.</b> The .xlsx was built on the reasoning
/// that a client asking « what does 6 000 € cover » should get the answer in the envelope. That was
/// right. What was wrong was the artefact: this page is read by a client, sometimes by a bâtonnier
/// and occasionally by a juge taxateur, and a spreadsheet is not a document one sends to any of them.
/// She was rebuilding it by hand in Word every time, which is the definition of a feature that does
/// not work.</para>
///
/// <para>The layout is her own, taken from the document she had been retyping: a title, the dossier
/// on one line, the period covered, three columns, and a closing sentence that states the arithmetic
/// in full so nobody has to reconstruct it.</para>
/// </summary>
public static class BillingDetailDocument
{
    /// <summary>
    /// Decimal hours with a comma: « 0,75 », « 2,00 », « 20,25 ».
    ///
    /// <para>Not « 45 min ». A convention d'honoraires is written in decimal hours, the client
    /// multiplies the column by the rate to check the total, and « 45 min » makes that arithmetic
    /// impossible to follow. Two decimals always, so the column reads as a column.</para>
    /// </summary>
    public static string Hours(int minutes) =>
        (minutes / 60m).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>
    /// « 4.050 € HT ». Her template groups thousands with a point, which is unusual in French and is
    /// what she sends her clients, so it is what this writes.
    /// </summary>
    public static string Euros(long cents)
    {
        var whole = cents / 100;
        var remainder = cents % 100;
        var grouped = whole.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.');

        return remainder == 0
            ? $"{grouped} €"
            : $"{grouped},{remainder:00} €";
    }

    /// <summary>« 25 août », « 7 septembre 2026 ». The year only where it is needed.</summary>
    public static string Day(DateOnly date, bool withYear) =>
        withYear
            ? $"{date.Day} {Months[date.Month - 1]} {date.Year}"
            : $"{date.Day} {Months[date.Month - 1]}";

    private static readonly string[] Months =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    /// <summary>
    /// « Diligences réalisées entre le 25 août et le 7 septembre 2026 », or « le 3 mars 2026 » when
    /// everything happened on one day, because a range whose ends are equal reads as a mistake.
    /// </summary>
    public static string Period(DateOnly first, DateOnly last) =>
        first == last
            ? $"Diligences réalisées le {Day(first, withYear: true)}"
            : $"Diligences réalisées entre le {Day(first, withYear: first.Year != last.Year)} et le {Day(last, withYear: true)}";

    /// <summary>
    /// The closing sentence. It states the hours, the rate and the product, so that the client can
    /// check it and so that nothing has to be taken on trust.
    ///
    /// <para>The rate is named only when every line shares it. With a dérogation in the list there is
    /// no single « taux horaire de X », and printing one anyway would be a figure that does not
    /// multiply out. The total then stands on its own, which is honest and still checkable line by
    /// line.</para>
    /// </summary>
    public static string Total(IReadOnlyCollection<DetailLine> lines)
    {
        var minutes = lines.Sum(line => line.Minutes);
        var cents = lines.Sum(line => line.RateCents * line.Minutes / 60);
        var rates = lines.Select(line => line.RateCents).Distinct().ToList();

        var sentence = new StringBuilder($"TOTAL : {Euros(cents)} HT correspondant à {Hours(minutes)} heures");

        if (rates.Count == 1)
        {
            sentence.Append($" au taux horaire de {Euros(rates[0])} HT");
        }

        return sentence.ToString();
    }

    public static byte[] Build(
        string? invoiceReference,
        DateOnly invoiceDate,
        string matterReference,
        string matterName,
        IReadOnlyList<DetailLine> lines,
        int? discountBasisPoints,
        string? discountReason,
        long billedCents)
    {
        using var buffer = new MemoryStream();

        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            body.AppendChild(Heading(invoiceReference is null
                ? $"DÉTAIL FACTURE DU {invoiceDate:dd/MM/yyyy}"
                : $"DÉTAIL FACTURE N° {invoiceReference}"));

            body.AppendChild(Text($"{matterReference} / {matterName}", bold: true));

            if (lines.Count > 0)
            {
                body.AppendChild(Text(Period(lines.Min(l => l.Date), lines.Max(l => l.Date))));
            }

            body.AppendChild(Text(string.Empty));
            body.AppendChild(Table(lines));
            body.AppendChild(Text(string.Empty));

            if (lines.Count > 0)
            {
                body.AppendChild(Text(Total(lines), bold: true));
            }

            // The remise, in the two lines her own document used: what was applied, and what it comes
            // to. Only when there is one, so an ordinary facture ends on its total.
            if (discountBasisPoints is > 0)
            {
                var percent = (discountBasisPoints.Value / 100m)
                    .ToString("0.##", CultureInfo.InvariantCulture)
                    .Replace('.', ',');

                body.AppendChild(Text(
                    $"Application d’une remise commerciale de {percent}%"
                    + (string.IsNullOrWhiteSpace(discountReason) ? string.Empty : $" {discountReason.Trim()}")));

                body.AppendChild(Text($"Ramené à {Euros(billedCents)} HT", bold: true));
            }

            main.Document.Save();
        }

        return buffer.ToArray();
    }

    private static Paragraph Heading(string text) =>
        new(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new RunProperties(new Bold(), new FontSize { Val = "28" }), new Text(text)));

    private static Paragraph Text(string text, bool bold = false)
    {
        var run = new Run();

        if (bold)
        {
            run.AppendChild(new RunProperties(new Bold()));
        }

        // Preserve, so an empty paragraph stays a blank line rather than collapsing.
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        return new Paragraph(run);
    }

    private static Table Table(IReadOnlyList<DetailLine> lines)
    {
        var table = new DocumentFormat.OpenXml.Wordprocessing.Table(
            new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        table.AppendChild(Row(["Date", "Temps passé (heure)", "Observations"], header: true));

        foreach (var line in lines)
        {
            table.AppendChild(Row(
                [line.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Hours(line.Minutes), line.Task],
                header: false));
        }

        return table;
    }

    private static TableRow Row(string[] cells, bool header)
    {
        var row = new TableRow();

        // Proportional, and the observation gets what is left: it is a sentence and the other two
        // are a date and a number.
        string[] widths = ["1400", "1600", "6000"];

        for (var index = 0; index < cells.Length; index++)
        {
            var run = new Run();

            if (header)
            {
                run.AppendChild(new RunProperties(new Bold()));
            }

            run.AppendChild(new Text(cells[index]) { Space = SpaceProcessingModeValues.Preserve });

            row.AppendChild(new TableCell(
                new TableCellProperties(new TableCellWidth { Width = widths[index], Type = TableWidthUnitValues.Dxa }),
                new Paragraph(run)));
        }

        return row;
    }
}
