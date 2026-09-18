using System.IO.Compression;
using System.Text;
using Avocado.Server.Features.Billings.Infrastructure;
using Avocado.Server.Features.Templates.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Avocado.Server.Tests.Templates;

/// <summary>
/// A cabinet's own détail de facture, filled from its own .docx.
///
/// <para>The row that repeats is the whole point: everything above and below it is the cabinet's
/// letterhead and closing sentence, and must come through untouched.</para>
/// </summary>
public class RepeatingRowTests
{
    /// <summary>
    /// A document shaped like the one they sent: a title, a table with a header row and one model
    /// row, and a closing line.
    /// </summary>
    private static byte[] Template()
    {
        using var buffer = new MemoryStream();

        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            body.AppendChild(Line("DETAIL FACTURE N° {{facture.numero}}"));
            body.AppendChild(Line("{{dossier.reference}} / {{dossier.nom}}"));
            body.AppendChild(Line("{{periode}}"));

            var table = body.AppendChild(new Table());
            table.AppendChild(Row("Date", "Temps passé (heure)", "Observations"));
            table.AppendChild(Row("{{ligne.date}}", "{{ligne.temps}}", "{{ligne.observations}}"));

            body.AppendChild(Line("TOTAL : {{total}} HT correspondant à {{heures}} heures au taux horaire de {{taux}} HT"));

            // As her own document writes it: the repère sits inside a French sentence, so the line
            // is not « only repères » and only the optional set can remove it.
            body.AppendChild(Line("Application d'une remise commerciale de {{remise.taux}} pour fidélité client"));
            body.AppendChild(Line("{{remise.ramene}}"));
            body.AppendChild(Line("N° RG : {{dossier.rg}}"));

            main.Document.Save();
        }

        return buffer.ToArray();
    }

    private static Paragraph Line(string text) =>
        new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static TableRow Row(params string[] cells) =>
        new(cells.Select(cell => new TableCell(Line(cell))));

    private static DetailLine Diligence(int day, int minutes, string task) =>
        new(new DateOnly(2026, 9, day), minutes, task, 20_000);

    private static string Read(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        using var stream = archive.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "<[^>]+>", string.Empty));
    }

    private static byte[] Fill(
        IReadOnlyList<DetailLine> lines,
        int? discount = null,
        string? reason = null,
        string? number = "90",
        string? rg = "23/04567")
    {
        using var template = new MemoryStream(Template());

        return TemplateMerge.Fill(
            template,
            BillingDetailTemplate.Values(
                number, new DateOnly(2026, 9, 8), "700770-COULEYRE", "EDF ENR", rg,
                lines, discount, reason, lines.Sum(line => line.RateCents * line.Minutes / 60)),
            BillingDetailTemplate.Rows(lines),
            BillingDetailTemplate.RowPrefix,
            BillingDetailTemplate.Optional);
    }

    [Fact]
    public void RepeatsTheRowOncePerDiligence()
    {
        var text = Read(Fill(
        [
            Diligence(1, 45, "Prise de connaissance du projet de courrier"),
            Diligence(2, 120, "Relecture conclusions n°3"),
            Diligence(7, 60, "Revue des nouvelles pièces"),
        ]));

        Assert.Contains("01/09/2026", text);
        Assert.Contains("02/09/2026", text);
        Assert.Contains("07/09/2026", text);
        Assert.Contains("0,75", text);
        Assert.Contains("2,00", text);
        Assert.Contains("Relecture conclusions n°3", text);

        // The model row is gone: leaving it would print « {{ligne.date}} » under every détail.
        Assert.DoesNotContain("{{ligne.", text);
    }

    [Fact]
    public void LeavesHerLetterheadAndClosingSentenceAlone()
    {
        var text = Read(Fill([Diligence(1, 1215, "Diligences")]));

        Assert.Contains("DETAIL FACTURE N° 90", text);
        Assert.Contains("700770-COULEYRE / EDF ENR", text);
        Assert.Contains("Diligences réalisées le 1 septembre 2026", text);
        Assert.Contains("TOTAL : 4.050 € HT correspondant à 20,25 heures au taux horaire de 200 € HT", text);

        // The header row of her table is not a repeating row and must survive.
        Assert.Contains("Temps passé (heure)", text);
        Assert.Contains("Observations", text);
    }

    /// <summary>
    /// The one that reached a client: « Application d'une remise commerciale de  pour fidélité
    /// client » printed on every détail, because filling an empty value leaves the sentence around
    /// it intact. A line that was nothing but repères, and whose repères are all empty, says nothing
    /// and is removed.
    /// </summary>
    [Fact]
    public void LeavesNoTraceOfTheRemiseWhenThereIsNone()
    {
        var text = Read(Fill([Diligence(1, 60, "Diligences")]));

        Assert.DoesNotContain("remise", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ramené", text);
        // And the lines that do carry text are untouched.
        Assert.Contains("TOTAL :", text);
        Assert.Contains("DETAIL FACTURE", text);
    }

    [Fact]
    public void WritesTheWholeRemiseSentenceWhenThereIsOne()
    {
        var text = Read(Fill([Diligence(1, 1215, "Diligences")], discount: 3_000, reason: "pour fidélité client"));

        Assert.Contains("Application d'une remise commerciale de 30% pour fidélité client", text);
        Assert.Contains("Ramené à", text);
    }

    /// <summary>
    /// « N° RG : » followed by nothing, on every dossier that never went to court.
    /// </summary>
    [Fact]
    public void DropsTheRgLineOnADossierThatHasNone()
    {
        Assert.Contains("N° RG : 23/04567", Read(Fill([Diligence(1, 60, "x")])));
        Assert.DoesNotContain("N° RG", Read(Fill([Diligence(1, 60, "x")], rg: null)));
    }

    /// <summary>
    /// The TOTAL line mentions {{taux}}, which is empty when the diligences do not share one rate.
    /// It must survive: it states the figure the whole page exists for.
    /// </summary>
    [Fact]
    public void KeepsTheTotalLineEvenWhenTheRateIsEmpty()
    {
        var mixed = new List<DetailLine>
        {
            new(new DateOnly(2026, 9, 1), 60, "Au taux du dossier", 20_000),
            new(new DateOnly(2026, 9, 2), 60, "Au taux dérogatoire", 15_000),
        };

        var text = Read(Fill(mixed));

        Assert.Contains("TOTAL : 350 € HT", text);
    }

    /// <summary>
    /// A facture with no number printed « DETAIL FACTURE N° » and then nothing. The intitulé carries
    /// its own wording so the heading reads either way.
    /// </summary>
    [Fact]
    public void NamesAFactureThatHasNoNumberByItsDateInstead()
    {
        var withNumber = BillingDetailTemplate.Values(
            "90", new DateOnly(2026, 9, 8), "R", "N", null, [Diligence(1, 60, "x")], null, null, 20_000);

        var without = BillingDetailTemplate.Values(
            null, new DateOnly(2026, 9, 8), "R", "N", null, [Diligence(1, 60, "x")], null, null, 20_000);

        Assert.Equal("n° 90", withNumber["facture.intitule"]);
        Assert.Equal("du 08/09/2026", without["facture.intitule"]);
        Assert.Equal(string.Empty, without["facture.numero"]);
    }

    [Fact]
    public void PrintsNoRowRatherThanAnEmptyOneWhenThereAreNoDiligences()
    {
        var text = Read(Fill([]));

        Assert.DoesNotContain("{{ligne.", text);
        Assert.Contains("Temps passé (heure)", text);
    }

    /// <summary>
    /// With a dérogation among the lines there is no single rate, so « taux » is empty rather than a
    /// figure that does not multiply out. Her sentence then reads oddly, which is her cue to reword
    /// it; a wrong number would not be.
    /// </summary>
    [Fact]
    public void LeavesTheRateEmptyWhenTheLinesDoNotShareOne()
    {
        var mixed = new List<DetailLine>
        {
            new(new DateOnly(2026, 9, 1), 60, "Au taux du dossier", 20_000),
            new(new DateOnly(2026, 9, 2), 60, "Au taux dérogatoire", 15_000),
        };

        var values = BillingDetailTemplate.Values(
            "90", new DateOnly(2026, 9, 8), "R", "N", null, mixed, null, null, 35_000);

        Assert.Equal(string.Empty, values["taux"]);
        Assert.Equal("350 €", values["total"]);
    }

    [Fact]
    public void OffersEveryFieldItFills()
    {
        var values = BillingDetailTemplate.Values(
            "90", new DateOnly(2026, 9, 8), "R", "N", "23/04567",
            [Diligence(1, 60, "x")], 3_000, "pour fidélité client", 14_000);

        // Every catalogue entry that is not a row field has to be a key the merge will actually
        // find, or the Modèles screen is advertising placeholders that come out blank.
        foreach (var (field, _) in BillingDetailTemplate.Catalogue)
        {
            if (field.StartsWith($"{BillingDetailTemplate.RowPrefix}.", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(values.ContainsKey(field), $"« {field} » is offered but never filled.");
        }

        Assert.Equal("30%", values["remise.taux"]);
        Assert.Equal("pour fidélité client", values["remise.motif"]);
        Assert.Equal("140 €", values["montant.facture"]);
        Assert.Equal("23/04567", values["dossier.rg"]);
    }

    [Fact]
    public void NamesEveryRowFieldItFills()
    {
        var rows = BillingDetailTemplate.Rows([Diligence(1, 45, "Prise de connaissance")]);

        foreach (var (field, _) in BillingDetailTemplate.Catalogue)
        {
            if (!field.StartsWith($"{BillingDetailTemplate.RowPrefix}.", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(rows[0].ContainsKey(field), $"« {field} » is offered but never filled.");
        }

        Assert.Equal("1", rows[0]["ligne.numero"]);
        Assert.Equal("0,75", rows[0]["ligne.temps"]);
    }
}
