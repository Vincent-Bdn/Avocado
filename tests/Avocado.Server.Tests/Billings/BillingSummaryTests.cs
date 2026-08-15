using Avocado.Server.Features.Billings.ValueObjects;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// The arithmetic behind « reste à facturer », the boni/mali and the net. It is the figure a practice
/// runs on, it is computed on read from six inputs that each mean something different, and the ways
/// it can be quietly wrong all look plausible on screen. So each rule is pinned to the reason it
/// exists rather than to the formula, which is what makes a failing test here readable.
/// </summary>
public class BillingSummaryTests
{
    private static BillingSummary Compute(
        long unbilledTime = 0,
        int unbilledMinutes = 0,
        long ledger = 0,
        long invoiced = 0,
        long manualInvoiced = 0,
        long variance = 0,
        long subcontracted = 0) =>
        BillingSummary.Compute(unbilledTime, unbilledMinutes, ledger, invoiced, manualInvoiced, variance, subcontracted);

    [Fact]
    public void AnUntouchedDossierOwesNothingAndIsOwedNothing()
    {
        var summary = Compute();

        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(0, summary.NetCents);
        Assert.Equal(0, summary.VarianceCents);
    }

    [Fact]
    public void UnbilledTimeIsWhatIsLeftToBill()
    {
        Assert.Equal(240_000, Compute(unbilledTime: 240_000, unbilledMinutes: 600).LeftToBillCents);
    }

    /// <summary>
    /// A provision already received reduces what remains to ask for. Otherwise the dossier invites
    /// billing the same work twice, once through the provision and once through the invoice.
    /// </summary>
    [Fact]
    public void AProvisionAlreadyReceivedReducesWhatIsLeft()
    {
        Assert.Equal(90_000, Compute(unbilledTime: 240_000, ledger: 150_000).LeftToBillCents);
    }

    /// <summary>
    /// A débours advanced for the client is a negative ledger, so it increases what is to be billed:
    /// the cabinet is out of pocket and re-bills it at cost.
    /// </summary>
    [Fact]
    public void ADeboursAdvancedIncreasesWhatIsLeft()
    {
        Assert.Equal(255_000, Compute(unbilledTime: 240_000, ledger: -15_000).LeftToBillCents);
    }

    /// <summary>
    /// A client in credit is a real state and shows as a negative figure. Clamping it to zero would
    /// hide money owed back, which is the direction a practice cannot afford to round.
    /// </summary>
    [Fact]
    public void AClientInCreditShowsAsNegativeRatherThanZero()
    {
        Assert.Equal(-60_000, Compute(unbilledTime: 90_000, ledger: 150_000).LeftToBillCents);
    }

    /// <summary>
    /// The distinction the whole record exists for. A facture built from selected hours already
    /// consumed those lines, so they are gone from the unbilled total; subtracting its amount as well
    /// would count the same work twice. Only invoices recorded by hand are taken off.
    /// </summary>
    [Fact]
    public void OnlyInvoicesRecordedByHandAreSubtracted()
    {
        var fromTime = Compute(unbilledTime: 100_000, invoiced: 300_000, manualInvoiced: 0);
        var byHand = Compute(unbilledTime: 100_000, invoiced: 300_000, manualInvoiced: 300_000);

        Assert.Equal(100_000, fromTime.LeftToBillCents);
        Assert.Equal(-200_000, byHand.LeftToBillCents);

        // Both were invoiced the same amount: the difference is only in what it consumed.
        Assert.Equal(fromTime.InvoicedCents, byHand.InvoicedCents);
    }

    /// <summary>
    /// Net is what the cabinet keeps once the confrères are paid. On a file largely handed over it is
    /// a very different number from what was invoiced, which is the reason it is carried at all.
    /// </summary>
    [Fact]
    public void NetIsWhatRemainsOnceTheConfreresArePaid()
    {
        Assert.Equal(180_000, Compute(invoiced: 300_000, subcontracted: 120_000).NetCents);
    }

    [Fact]
    public void NetEqualsInvoicedWhenNothingWasSubcontracted()
    {
        var summary = Compute(invoiced: 300_000);

        Assert.Equal(summary.InvoicedCents, summary.NetCents);
    }

    /// <summary>
    /// The invariant that took a session to get right. A rétrocession is a charge of the cabinet, not
    /// something advanced for the client: the client owes the full fee whoever did the work. Taking it
    /// off « reste à facturer » would understate the bill by exactly what she is about to pay away.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(50_000)]
    [InlineData(1_000_000)]
    public void SubcontractingNeverChangesWhatIsLeftToBill(long subcontracted)
    {
        var summary = Compute(unbilledTime: 240_000, ledger: 90_000, subcontracted: subcontracted);

        Assert.Equal(150_000, summary.LeftToBillCents);
    }

    /// <summary>Sous-traitance larger than the fee is a mali, and the figure is allowed to go negative.</summary>
    [Fact]
    public void NetGoesNegativeWhenTheRetrocessionExceedsTheFee()
    {
        Assert.Equal(-20_000, Compute(invoiced: 100_000, subcontracted: 120_000).NetCents);
    }

    [Fact]
    public void BoniAndMaliAreCarriedThroughWithTheirSign()
    {
        Assert.Equal(45_000, Compute(variance: 45_000).VarianceCents);
        Assert.Equal(-45_000, Compute(variance: -45_000).VarianceCents);
    }

    [Fact]
    public void TheMinutesBehindTheMoneyAreKept()
    {
        var summary = Compute(unbilledTime: 240_000, unbilledMinutes: 600);

        Assert.Equal(600, summary.BillableMinutes);
        Assert.Equal(240_000, summary.BillableTimeCents);
    }

    /// <summary>
    /// Everything at once, on a dossier that has had a real life: hours logged, a provision received,
    /// a débours advanced, a facture from time and one by hand, and part of it handed to a confrère.
    /// </summary>
    [Fact]
    public void HoldsTogetherOnADossierWithAHistory()
    {
        var summary = Compute(
            unbilledTime: 180_000,
            unbilledMinutes: 450,
            ledger: 200_000 - 35_000,
            invoiced: 500_000,
            manualInvoiced: 120_000,
            variance: -25_000,
            subcontracted: 150_000);

        Assert.Equal(180_000 - 165_000 - 120_000, summary.LeftToBillCents);
        Assert.Equal(350_000, summary.NetCents);
        Assert.Equal(-25_000, summary.VarianceCents);
    }
}
