using Avocado.Server.Features.Imports.Infrastructure;
using ClosedXML.Excel;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// export.xlsx, the account Gestisoft prints for one dossier: twenty-three columns of which eight
/// matter, written by a reporting tool that formats none of them.
/// </summary>
public class GestisoftBillingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"avocado-billing-{Guid.NewGuid():N}");

    public GestisoftBillingTests() => Directory.CreateDirectory(_folder);

    /// <summary>
    /// Writes a sheet from a header row and cell values, in the shapes the real file uses: dates as
    /// bare serial numbers, amounts as the doubles they round-trip to.
    /// </summary>
    private string Sheet(string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Export");

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                var cell = sheet.Cell(row + 2, column + 1);

                switch (rows[row][column])
                {
                    case null: break;
                    case double number: cell.Value = number; break;
                    case string text: cell.Value = text; break;
                    default: cell.Value = rows[row][column]!.ToString(); break;
                }
            }
        }

        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);

        return path;
    }

    private static readonly string[] Headers =
        ["Date", "Processus", "Débit", "Crédit", "Pointage", "Facture", "Solde", "H.T.", "T.V.A.", "Libellé", "Code Dossier"];

    /// <summary>A facture and the règlement that settles it, as Gestisoft writes the pair.</summary>
    private string Settled() => Sheet(
        Headers,
        [46080d, "Factures", 7800d, 0d, "202604792", "202604792", 0d, 6500d, 1300d, "Facture", "700978"],
        [46091d, "Règlements clients", 0d, 7800d, "202604792", null, 0d, 6500d, 1300d, "Règlements clients", "700978"]);

    [Fact]
    public void ReadsAFactureAndTheReglementThatSettlesIt()
    {
        var rows = GestisoftBilling.Read(Settled());

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsInvoice);
        Assert.True(rows[1].IsPayment);
        Assert.Equal("202604792", rows[1].Pointage);
    }

    /// <summary>
    /// The dates arrive as bare serial numbers, because the reporting tool writes the value without the
    /// format that would make Excel show it as a date.
    /// </summary>
    [Fact]
    public void ReadsADateThatExcelNeverFormattedAsOne() =>
        Assert.Equal(new DateOnly(2026, 2, 27), GestisoftBilling.Read(Settled())[0].Date);

    /// <summary>
    /// The file holds binary doubles, so 266,40 is stored as 266.39999999999998. Truncating loses a
    /// centime on a fair proportion of the rows.
    /// </summary>
    [Fact]
    public void RoundsAnAmountThatCannotBeWrittenExactly()
    {
        var path = Sheet(
            Headers,
            [46227d, "Factures", 1598.4d, 0d, "202615683", "202615683", 1598.4d, 1332d, 266.39999999999998d, "Facture", "700978"]);

        var row = Assert.Single(GestisoftBilling.Read(path));

        Assert.Equal(26_640, row.VatCents);
        Assert.Equal(133_200, row.ExclVatCents);
        Assert.Equal(159_840, row.DebitCents);
    }

    /// <summary>The export is produced by hand and there is no reason the next one keeps this order.</summary>
    [Fact]
    public void ReadsTheColumnsByNameRatherThanByPosition()
    {
        var path = Sheet(
            ["Code Dossier", "T.V.A.", "Libellé", "H.T.", "Débit", "Date"],
            ["700978", 300d, "Facture", 1500d, 1800d, 46169d]);

        var row = Assert.Single(GestisoftBilling.Read(path));

        Assert.Equal("700978", row.DossierCode);
        Assert.Equal(150_000, row.ExclVatCents);
        Assert.Equal(30_000, row.VatCents);
        Assert.Equal(new DateOnly(2026, 5, 27), row.Date);
    }

    /// <summary>
    /// The accented headers, on their own, because they are the ones that broke.
    ///
    /// <para>Folding them with Unicode normalisation works everywhere except where Avocado runs:
    /// published with InvariantGlobalization, Normalize is a no-op, « Débit » never matched « debit »
    /// and every accented money column read as zero. Nothing threw. A dossier imported with no facture
    /// at all is the worst thing this module can do, so the fold is pinned here.</para>
    /// </summary>
    [Theory]
    [InlineData("Débit")]
    [InlineData("DÉBIT")]
    [InlineData("debit")]
    public void MatchesAHeaderWhateverAccentsItCarries(string header)
    {
        var path = Sheet(["Code Dossier", header], ["700978", 1800d]);

        Assert.Equal(180_000, Assert.Single(GestisoftBilling.Read(path)).DebitCents);
    }

    [Fact]
    public void SkipsThePaddingRowsGestisoftLeavesAtTheBottom()
    {
        var path = Sheet(
            Headers,
            [46080d, "Factures", 7800d, 0d, "202604792", "202604792", 0d, 6500d, 1300d, "Facture", "700978"],
            [null, null, null, null, null, null, null, null, null, null, null],
            [null, null, 0d, 0d, null, null, null, null, null, null, null]);

        Assert.Single(GestisoftBilling.Read(path));
    }

    /// <summary>
    /// The scan finds the spreadsheet by its name and its folder, so it can land on a client's own
    /// workbook. Reading one must give nothing, not throw.
    /// </summary>
    [Fact]
    public void ReturnsNothingForAWorkbookThatIsNotAnExport()
    {
        Assert.Empty(GestisoftBilling.Read(Sheet(["Chantier", "Marché"], ["Toiture", "Lot 3"])));
        Assert.Empty(GestisoftBilling.Read(Path.Combine(_folder, "absent.xlsx")));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
