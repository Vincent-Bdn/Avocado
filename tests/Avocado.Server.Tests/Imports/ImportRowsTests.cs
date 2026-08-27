using Avocado.Server.Features.Imports;
using Avocado.Server.Features.Imports.Infrastructure;
using ClosedXML.Excel;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// What a dossier should carry once its sources have been reconciled, which is where this module can
/// be wrong in a way nobody notices. A file that fails to parse is obvious; a facture counted twice is
/// a trésorerie figure that looks entirely plausible and is not.
/// </summary>
public class ImportRowsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"avocado-rows-{Guid.NewGuid():N}");

    public ImportRowsTests() => Directory.CreateDirectory(_folder);

    private static readonly Sidecars Nothing = new([], [], []);

    private ImportCandidate Candidate(string? billing = null, string? contacts = null) =>
        new(_folder, "ANODEA", "TMF c SILLAND", true, 1, 0, 0, 0, "700978", contacts, billing);

    /// <summary>A facture, and the règlement Gestisoft writes beneath it sharing a pointage.</summary>
    private string Account(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Export");

        string[] headers = ["Date", "Processus", "Débit", "Crédit", "Pointage", "Facture", "Solde", "H.T.", "Libellé", "Code Dossier"];

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                var cell = sheet.Cell(row + 2, column + 1);

                if (rows[row][column] is double number)
                {
                    cell.Value = number;
                }
                else if (rows[row][column] is string text)
                {
                    cell.Value = text;
                }
            }
        }

        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);

        return path;
    }

    /// <summary>
    /// <b>The rule this file exists for.</b> The facture and the payment that settles it are two rows
    /// in the export. Recording the payment as a movement as well as marking the facture paid counts
    /// the money twice, and the total still adds up, so nothing looks wrong.
    /// </summary>
    [Fact]
    public void SettlesAFactureRatherThanRecordingItsPaymentTwice()
    {
        var money = ImportRows.Money(
            Candidate(Account(
                [46080d, "Factures", 7800d, 0d, "202604792", "202604792", 0d, 6500d, "Facture", "700978"],
                [46091d, "Règlements clients", 0d, 7800d, "202604792", null, 0d, 6500d, "Règlements clients", "700978"])),
            Nothing);

        var invoice = Assert.Single(money);

        Assert.True(invoice.IsInvoice);
        Assert.True(invoice.IsPaid);
        Assert.Equal(new DateOnly(2026, 3, 10), invoice.PaidOn);
        Assert.Equal(650_000, invoice.AmountCents);
    }

    /// <summary>
    /// Invoiced at 7 581 €, part paid at 5 000 €, 2 581 € still owed. Reading the payment as proof of
    /// settlement loses the balance she is owed, which is the figure she opens the application for.
    /// </summary>
    [Fact]
    public void LeavesAPartPaidFactureUnpaid()
    {
        var money = ImportRows.Money(
            Candidate(Account(
                [46022d, "Factures", 7581d, 0d, "202524764", "202524764", 2581d, 6317.5d, "Facture", "149860"],
                [46174d, "Règlements clients", 0d, 5000d, "202524764", null, 0d, 4166.67d, "Règlements clients", "149860"])),
            Nothing);

        Assert.False(Assert.Single(money).IsPaid);
    }

    /// <summary>A payment settling nothing is money that came in, and must not be lost.</summary>
    [Fact]
    public void KeepsAPaymentThatBelongsToNoFacture()
    {
        var money = ImportRows.Money(
            Candidate(Account(
                [46091d, "Règlements clients", 0d, 150000d, "202600001", null, 0d, 125000d, "Provision", "700978"])),
            Nothing);

        var movement = Assert.Single(money);

        Assert.False(movement.IsInvoice);
        Assert.False(movement.IsDisbursement);
        Assert.Equal(15_000_000, movement.AmountCents);
    }

    /// <summary>
    /// The amount on a facture is the H.T., not the débit: the débit is what the client owes, which
    /// includes the T.V.A. and any débours passed on, and the invoice records what was billed.
    /// </summary>
    [Fact]
    public void RecordsTheAmountExclusiveOfTax() =>
        Assert.Equal(
            650_000,
            Assert.Single(ImportRows.Money(
                Candidate(Account([46080d, "Factures", 7800d, 0d, "202604792", "202604792", 0d, 6500d, "Facture", "700978"])),
                Nothing)).AmountCents);

    /// <summary>
    /// A débours entered as a positive number makes every balance on the dossier wrong while looking
    /// entirely plausible, so the direction comes from the words rather than from the number.
    /// </summary>
    [Theory]
    [InlineData("Débours", true)]
    [InlineData("débours", true)]
    [InlineData("Encaissement", false)]
    [InlineData("Provision", false)]
    public void TakesTheDirectionOfAMovementFromWhatSheCalledIt(string kind, bool disbursement)
    {
        var sidecars = new Sidecars(
            [],
            [new FacturationRow("TMF c SILLAND", new DateOnly(2026, 3, 1), kind, 12_000, "Greffe", "", false)],
            []);

        Assert.Equal(disbursement, Assert.Single(ImportRows.Money(Candidate(), sidecars)).IsDisbursement);
    }

    /// <summary>She types a facture into the spreadsheet as well as exporting it, when she is unsure.</summary>
    [Fact]
    public void DoesNotAddAFactureTheExportAlreadyCarries()
    {
        var sidecars = new Sidecars(
            [],
            [new FacturationRow("TMF c SILLAND", new DateOnly(2026, 2, 27), "Facture", 650_000, "Facture", "202604792", true)],
            []);

        var billing = Account([46080d, "Factures", 7800d, 0d, "202604792", "202604792", 0d, 6500d, "Facture", "700978"]);

        Assert.Single(ImportRows.Money(Candidate(billing), sidecars));
    }

    /// <summary>A dossier Gestisoft refused to export has only what she typed, and it must still arrive.</summary>
    [Fact]
    public void FallsBackToTheSpreadsheetWhenThereIsNoExport()
    {
        var sidecars = new Sidecars(
            [new TiersRow("TMF c SILLAND", "SARL Dupont", "PM", "Client", "contact@dupont.fr", "", "")],
            [],
            []);

        var party = Assert.Single(ImportRows.Parties(Candidate(), contacts: null, sidecars));

        Assert.Equal("SARL Dupont", party.Name);
        Assert.True(party.IsClient);
        Assert.True(party.IsOrganisation);
    }

    /// <summary>
    /// A name in both sources is taken from the contacts list, since that is the one that was not
    /// retyped, and a second fiche for the same person is a carnet with a duplicate in it from day one.
    /// </summary>
    [Fact]
    public void PrefersTheContactsListToWhatSheRetyped()
    {
        var contacts = new ContactsList(
            "700978",
            "TELE MEDECINS DE FRANCE",
            [new GestisoftContact("TELE MEDECINS DE FRANCE", "Client", true, true, "33000", "BORDEAUX", null, null)]);

        var sidecars = new Sidecars(
            [new TiersRow("TMF c SILLAND", "TELE MEDECINS DE FRANCE", "PM", "Client", "retyped@tmf.fr", "", "")],
            [],
            []);

        var party = Assert.Single(ImportRows.Parties(Candidate(), contacts, sidecars));

        Assert.Null(party.Email);
        Assert.Equal("33000 BORDEAUX", party.Address);
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
