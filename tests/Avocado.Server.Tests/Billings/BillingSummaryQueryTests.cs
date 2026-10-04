using Avocado.Server.Data;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.ValueObjects;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// One dossier's figures against a real database rather than against their arithmetic.
///
/// <para>The arithmetic was never the hard part. What went wrong twice was which rows were fed to
/// it, and that decision lives in a LINQ filter no pure test can reach. A dossier imported from
/// Gestisoft with seven paid factures reported « reste à facturer − 8 974 € », and the first 45
/// minutes recorded on it made that − 8 794 €.</para>
///
/// <para>Against a <see cref="TestVault"/>: unencrypted SQLite in memory, since what is being tested
/// is a query and not a vault.</para>
/// </summary>
public class BillingSummaryQueryTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly Matter _matter;

    public BillingSummaryQueryTests() =>
        _matter = _vault.Save(new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2025, 1, 6),
            HourlyRateCents = 24_000,
        });

    private BillingInvoice Invoice(
        long amountExclVatCents,
        bool historical = false,
        long billedTimeCents = 0,
        bool paid = false,
        bool abandon = false,
        bool legacy = false) =>
        _vault.Save(new BillingInvoice
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2025, 7, 3),
            AmountExclVatCents = amountExclVatCents,
            BilledTimeCents = billedTimeCents,
            IsHistorical = historical,
            IsPaid = paid,
            IsAbandon = abandon,
            DeductsWithoutLines = legacy,
        });

    /// <summary>Minutes still waiting, or already taken by a facture.</summary>
    private void Minutes(int minutes, BillingInvoice? on = null) =>
        _vault.Save(new TimeEntry
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 8, 27),
            DurationMinutes = minutes,
            Task = "Diligences",
            IsBillable = true,
            InvoiceId = on?.Id,
        });

    private void Ledger(long amountCents) =>
        _vault.Save(new BillingLedgerEntry
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 2, 1),
            AmountCents = amountCents,
            Label = "Provision",
        });

    private void Forfait(long cents)
    {
        _matter.FixedFeeCents = cents;
        _vault.Database.SaveChanges();
    }

    private BillingSummary Summary() =>
        BillingSummaryQuery.ForMatterAsync(_vault.Database, _matter.Id, default).GetAwaiter().GetResult();

    /// <summary>
    /// Seven factures brought over from Gestisoft, 8 974 € in all, and 45 minutes recorded
    /// afterwards: what is left to bill is those 45 minutes and nothing else.
    /// </summary>
    [Fact]
    public void LetsImportedTurnoverAlone()
    {
        foreach (var amount in new long[] { 260_000, 100_000, 291_000, 128_000, 15_000, 50_000, 53_400 })
        {
            Invoice(amount, historical: true);
        }

        Assert.Equal(0, Summary().LeftToBillCents);

        Minutes(45);

        var summary = Summary();

        Assert.Equal(18_000, summary.LeftToBillCents);
        Assert.Equal(897_400, summary.InvoicedCents);
        Assert.Equal(0, summary.VarianceCents);
    }

    /// <summary>
    /// A facture takes its lines with it, so what is left is the time it did not take. No subtraction
    /// of the amount, which is what used to double-count.
    /// </summary>
    [Fact]
    public void LeavesTheTimeAFactureDidNotTake()
    {
        var facture = Invoice(60_000, billedTimeCents: 50_000);

        Minutes(125, on: facture);  // 50 000 c worth, consumed
        Minutes(300);               // 120 000 c worth, waiting

        var summary = Summary();

        Assert.Equal(170_000, summary.WorkedCents);
        Assert.Equal(120_000, summary.UnbilledTimeCents);
        Assert.Equal(120_000, summary.LeftToBillCents);
        Assert.Equal(10_000, summary.VarianceCents);
    }

    /// <summary>
    /// « Je négocie 3 000 euros, je rentre 4 000 euros de temps, donc 1 000 de mali, reste à
    /// facturer 0. » Through the database this time, forfait included.
    /// </summary>
    [Fact]
    public void AnswersTheForfaitThroughTheWholeQuery()
    {
        Forfait(300_000);

        var facture = Invoice(300_000, billedTimeCents: 400_000, paid: true);
        Minutes(1_000, on: facture);

        var summary = Summary();

        Assert.Equal(400_000, summary.WorkedCents);
        Assert.Equal(300_000, summary.InvoicedCents);
        Assert.Equal(-100_000, summary.VarianceCents);
        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(300_000, summary.CollectedCents);
        Assert.Equal(0, summary.OwedCents);
    }

    /// <summary>Working past a forfait leaves a mali and nothing to bill, never a debt.</summary>
    [Fact]
    public void KeepsAForfaitClosedWhenSheWorksOnPastIt()
    {
        Forfait(300_000);

        var facture = Invoice(300_000, billedTimeCents: 300_000);
        Minutes(750, on: facture);
        Minutes(200);

        var summary = Summary();

        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(80_000, summary.UnbilledTimeCents);
    }

    /// <summary>
    /// A provision is cash, not billing. It used to come off « reste à facturer » for the dossier's
    /// life while the time it stood against reset at every facture.
    /// </summary>
    [Fact]
    public void KeepsAProvisionOutOfWhatIsLeftToBill()
    {
        Minutes(500);
        Ledger(100_000);

        var summary = Summary();

        Assert.Equal(200_000, summary.LeftToBillCents);
        Assert.Equal(100_000, summary.CollectedCents);
        Assert.Equal(-100_000, summary.OwedCents);
        Assert.Equal(0, summary.DisbursementsCents);
    }

    [Fact]
    public void ShowsWhatSheAdvancedAsItsOwnFigure()
    {
        Minutes(500);
        Ledger(-45_000);

        var summary = Summary();

        Assert.Equal(45_000, summary.DisbursementsCents);
        Assert.Equal(200_000, summary.LeftToBillCents);
        Assert.Equal(0, summary.CollectedCents);
    }

    /// <summary>
    /// « J'ai 1 500 en temps passé, j'ai facturé 1 000 et ne pourrai pas facturer plus. » The abandon
    /// takes the remaining lines, is a mali of exactly them, and is not turnover.
    /// </summary>
    [Fact]
    public void SettlesTheRestAsAMaliWhenSheWillNotBillIt()
    {
        var facture = Invoice(100_000, billedTimeCents: 100_000);
        Minutes(250, on: facture);

        var abandon = Invoice(0, billedTimeCents: 50_000, abandon: true);
        Minutes(125, on: abandon);

        var summary = Summary();

        Assert.Equal(150_000, summary.WorkedCents);
        Assert.Equal(100_000, summary.InvoicedCents);
        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(-50_000, summary.VarianceCents);
    }

    /// <summary>
    /// A facture recorded before factures took their lines. It consumed nothing and must go on
    /// subtracting, or every dossier carrying one jumps up the day they update.
    /// </summary>
    [Fact]
    public void HonoursAFactureFromBeforeTheModelChanged()
    {
        Minutes(500);
        Invoice(60_000, legacy: true);

        var summary = Summary();

        Assert.Equal(200_000, summary.UnbilledTimeCents);
        Assert.Equal(140_000, summary.LeftToBillCents);
    }

    [Fact]
    public void AddsPaidFacturesAndProvisionsIntoWhatCameIn()
    {
        Invoice(300_000, paid: true);
        Invoice(120_000);
        Ledger(50_000);

        var summary = Summary();

        Assert.Equal(420_000, summary.InvoicedCents);
        Assert.Equal(350_000, summary.CollectedCents);
        Assert.Equal(70_000, summary.OwedCents);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}
