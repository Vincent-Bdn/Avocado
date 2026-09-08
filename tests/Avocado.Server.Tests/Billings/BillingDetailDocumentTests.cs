using System.IO.Compression;
using System.Text;
using Avocado.Server.Features.Billings.Infrastructure;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// The détail that goes out with the facture, built to the layout she had been retyping by hand.
///
/// <para>Every figure on this page is one a client multiplies out to check. A duration rendered
/// wrongly is not a formatting bug, it is an invoice that does not add up in front of the person
/// paying it.</para>
/// </summary>
public class BillingDetailDocumentTests
{
    private static DetailLine Line(int day, int minutes, string task, long rate = 20_000) =>
        new(new DateOnly(2026, 9, day), minutes, task, rate);

    [Theory]
    [InlineData(15, "0,25")]
    [InlineData(30, "0,50")]
    [InlineData(45, "0,75")]
    [InlineData(60, "1,00")]
    [InlineData(120, "2,00")]
    [InlineData(180, "3,00")]
    [InlineData(1215, "20,25")]
    public void WritesDurationsInDecimalHoursTheWayHerTemplateDoes(int minutes, string expected) =>
        Assert.Equal(expected, BillingDetailDocument.Hours(minutes));

    [Theory]
    [InlineData(405_000, "4.050 €")]
    [InlineData(283_500, "2.835 €")]
    [InlineData(20_000, "200 €")]
    [InlineData(123_456, "1.234,56 €")]
    public void GroupsThousandsTheWayHerOwnDocumentDoes(long cents, string expected) =>
        Assert.Equal(expected, BillingDetailDocument.Euros(cents));

    [Fact]
    public void StatesThePeriodTheDiligencesCover()
    {
        Assert.Equal(
            "Diligences réalisées entre le 25 août et le 7 septembre 2026",
            BillingDetailDocument.Period(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void DoesNotWriteARangeForASingleDay()
    {
        // « entre le 3 mars et le 3 mars » reads as a mistake, and a client who spots one stops
        // trusting the rest of the page.
        Assert.Equal(
            "Diligences réalisées le 3 mars 2026",
            BillingDetailDocument.Period(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 3)));
    }

    [Fact]
    public void NamesBothYearsWhenTheWorkStraddlesThem()
    {
        Assert.Equal(
            "Diligences réalisées entre le 20 décembre 2026 et le 8 janvier 2027",
            BillingDetailDocument.Period(new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 8)));
    }

    /// <summary>The sentence from her own document, reproduced exactly.</summary>
    [Fact]
    public void ClosesOnTheArithmeticSpeltOut()
    {
        DetailLine[] lines =
        [
            Line(1, 45, "Prise de connaissance"),
            Line(2, 120, "Relecture conclusions n°3"),
            Line(3, 180, "Rédaction des conclusions n°4"),
            Line(4, 120, "Rédaction des conclusions n°4"),
            Line(5, 90, "Réunion de travail"),
            Line(6, 240, "Modification des conclusions"),
            Line(7, 360, "Modification des conclusions"),
            Line(8, 60, "Revue des nouvelles pièces"),
        ];

        Assert.Equal(1215, lines.Sum(line => line.Minutes));   // 20,25 h.
        Assert.Equal(
            "TOTAL : 4.050 € HT correspondant à 20,25 heures au taux horaire de 200 € HT",
            BillingDetailDocument.Total(lines));
    }

    [Fact]
    public void RefusesToNameOneRateWhenTheLinesDoNotShareOne()
    {
        DetailLine[] lines =
        [
            Line(1, 60, "Au taux du dossier"),
            Line(2, 60, "Au taux dérogatoire", rate: 15_000),
        ];

        var total = BillingDetailDocument.Total(lines);

        // 350 € for two hours multiplies out to no single rate, so printing one would be a figure the
        // client cannot verify. The total stands alone and the lines remain checkable one by one.
        Assert.Equal("TOTAL : 350 € HT correspondant à 2,00 heures", total);
        Assert.DoesNotContain("taux horaire", total);
    }

    [Fact]
    public void ProducesAWordDocumentCarryingEveryLine()
    {
        DetailLine[] lines =
        [
            Line(1, 45, "Prise de connaissance du projet de courrier"),
            Line(7, 60, "Revue des nouvelles pièces transmises"),
        ];

        var bytes = BillingDetailDocument.Build(
            invoiceReference: "90",
            invoiceDate: new DateOnly(2026, 9, 8),
            matterReference: "700770-COULEYRE",
            matterName: "EDF ENR",
            lines: lines,
            discountBasisPoints: null,
            discountReason: null,
            billedCents: 21_000);

        var text = ReadText(bytes);

        Assert.Contains("DÉTAIL FACTURE N° 90", text);
        Assert.Contains("700770-COULEYRE / EDF ENR", text);
        Assert.Contains("Diligences réalisées entre le 1 septembre et le 7 septembre 2026", text);
        Assert.Contains("Temps passé (heure)", text);
        Assert.Contains("Observations", text);
        Assert.Contains("01/09/2026", text);
        Assert.Contains("0,75", text);
        Assert.Contains("Prise de connaissance du projet de courrier", text);
        Assert.Contains("TOTAL : 350 € HT", text);
    }

    [Fact]
    public void WritesTheRemiseInTheTwoLinesSheUsed()
    {
        var bytes = BillingDetailDocument.Build(
            invoiceReference: "90",
            invoiceDate: new DateOnly(2026, 9, 8),
            matterReference: "700770-COULEYRE",
            matterName: "EDF ENR",
            lines: [Line(1, 1215, "Diligences")],
            discountBasisPoints: 3_000,
            discountReason: "pour fidélité client",
            billedCents: 283_500);

        var text = ReadText(bytes);

        Assert.Contains("TOTAL : 4.050 € HT", text);
        Assert.Contains("Application d’une remise commerciale de 30% pour fidélité client", text);
        Assert.Contains("Ramené à 2.835 € HT", text);
    }

    [Fact]
    public void SaysNothingAboutARemiseWhenThereIsNone()
    {
        var bytes = BillingDetailDocument.Build(
            invoiceReference: null,
            invoiceDate: new DateOnly(2026, 9, 8),
            matterReference: "2026-0001",
            matterName: "Durand",
            lines: [Line(1, 60, "Diligences")],
            discountBasisPoints: null,
            discountReason: null,
            billedCents: 20_000);

        var text = ReadText(bytes);

        Assert.DoesNotContain("remise", text);
        // No external reference yet: the facture is named by its date rather than by « N° ».
        Assert.Contains("DÉTAIL FACTURE DU 08/09/2026", text);
    }

    /// <summary>Reads the document part straight out of the .docx, which is a zip.</summary>
    private static string ReadText(byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var entry = archive.GetEntry("word/document.xml");
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        // Word splits a paragraph across runs, so the tags come out and the text is read as one.
        return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", string.Empty));
    }
}
