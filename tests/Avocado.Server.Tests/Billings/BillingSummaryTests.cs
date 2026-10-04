using Avocado.Server.Features.Billings.ValueObjects;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// The arithmetic of one dossier, pinned.
///
/// <para>Two lawyers independently reported the same thing: a forfait produced no mali and still
/// left money « à facturer ». Both of their examples are below, by name, because they are what this
/// model exists to answer correctly.</para>
/// </summary>
public class BillingSummaryTests
{
    private static BillingSummary Summary(
        long worked = 0,
        long unbilled = 0,
        long invoiced = 0,
        long collected = 0,
        long disbursements = 0,
        long variance = 0,
        long subcontracted = 0,
        long legacy = 0,
        long? forfait = null) =>
        BillingSummary.Compute(
            worked, unbilled, 0, invoiced, collected, disbursements, variance, subcontracted, legacy, forfait);

    /// <summary>
    /// « Je négocie 3 000 euros. Dans mes temps passés je rentre 4 000 euros, donc 1 000 de mali. Le
    /// client me paye 3 000. Reste à facturer 0, même si j'ai travaillé pour 1 000 euros de plus. »
    /// </summary>
    [Fact]
    public void AnswersTheForfaitExactlyAsSheStatedIt()
    {
        // She ticks every line, the override arrives pre-filled at the forfait, she accepts.
        var summary = Summary(
            worked: 400_000,
            unbilled: 0,
            invoiced: 300_000,
            collected: 300_000,
            variance: 300_000 - 400_000,
            forfait: 300_000);

        Assert.Equal(400_000, summary.WorkedCents);
        Assert.Equal(300_000, summary.InvoicedCents);
        Assert.Equal(-100_000, summary.VarianceCents);
        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(0, summary.OwedCents);
    }

    /// <summary>Four lines worth 1 300, a forfait of 1 000, and she bills the forfait.</summary>
    [Fact]
    public void TurnsTheDifferenceIntoAMaliWhateverSheBills()
    {
        var mali = Summary(worked: 130_000, invoiced: 100_000, variance: -30_000, forfait: 100_000);
        Assert.Equal(-30_000, mali.VarianceCents);
        Assert.Equal(0, mali.LeftToBillCents);

        // And the same subtraction the other way round when she bills above the lines.
        var boni = Summary(worked: 130_000, invoiced: 150_000, variance: 20_000, forfait: 100_000);
        Assert.Equal(20_000, boni.VarianceCents);
    }

    /// <summary>
    /// A forfait stops the hours driving what is left to bill. Working past it is a mali, never a debt
    /// the client owes, and never a figure she might invoice twice.
    /// </summary>
    [Fact]
    public void LeavesNothingToBillOnceTheForfaitIsBilled()
    {
        // She bills the forfait, then does another 500 € of work on top.
        var summary = Summary(worked: 450_000, unbilled: 50_000, invoiced: 300_000, forfait: 300_000);

        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(50_000, summary.UnbilledTimeCents);
    }

    [Fact]
    public void BillsHalfAForfaitAndLeavesTheOtherHalf()
    {
        Assert.Equal(150_000, Summary(invoiced: 150_000, forfait: 300_000).LeftToBillCents);
    }

    /// <summary>Billing past a forfait leaves nothing to bill, not a negative amount.</summary>
    [Fact]
    public void NeverGoesBelowZeroOnAForfait()
    {
        Assert.Equal(0, Summary(invoiced: 400_000, forfait: 300_000).LeftToBillCents);
    }

    [Fact]
    public void AtTheHourWhatIsLeftIsTheTimeNoFactureHasTaken()
    {
        var summary = Summary(worked: 200_000, unbilled: 80_000, invoiced: 120_000);

        Assert.Equal(80_000, summary.LeftToBillCents);
    }

    /// <summary>
    /// The incoherence this model was rewritten to remove. A provision used to come off « reste à
    /// facturer » for the life of the dossier while the time it was set against reset at every
    /// facture, so the figure drifted negative and the screen had a sentence explaining it away.
    /// </summary>
    [Fact]
    public void LeavesAProvisionOutOfWhatIsStillToBill()
    {
        var summary = Summary(worked: 200_000, unbilled: 200_000, invoiced: 0, collected: 100_000);

        Assert.Equal(200_000, summary.LeftToBillCents);

        // It is cash, and it says the client has paid in advance.
        Assert.Equal(100_000, summary.CollectedCents);
        Assert.Equal(-100_000, summary.OwedCents);
    }

    [Fact]
    public void KeepsDisbursementsAsTheirOwnFigure()
    {
        var summary = Summary(unbilled: 100_000, disbursements: 45_000);

        Assert.Equal(45_000, summary.DisbursementsCents);
        Assert.Equal(100_000, summary.LeftToBillCents);
    }

    /// <summary>
    /// « Reprises » consumed no lines, so they subtract nothing and report no gap. COULEYRE arrived
    /// with 8 974 € of them and read « reste à facturer − 8 794 € » on its first 45 minutes of work.
    /// </summary>
    [Fact]
    public void LetsImportedTurnoverAloneEntirely()
    {
        var summary = Summary(worked: 10_000, unbilled: 10_000, invoiced: 897_400, variance: 0);

        Assert.Equal(10_000, summary.LeftToBillCents);
        Assert.Equal(0, summary.VarianceCents);
        Assert.Equal(897_400, summary.InvoicedCents);
    }

    /// <summary>
    /// Factures recorded before factures consumed lines. They go on subtracting, because the hours
    /// they paid for are still sitting unbilled and nobody can say which ones they were.
    /// </summary>
    [Fact]
    public void HonoursAFactureWrittenBeforeFacturesTookTheirLines()
    {
        var summary = Summary(unbilled: 200_000, invoiced: 120_000, legacy: 120_000);

        Assert.Equal(80_000, summary.LeftToBillCents);
    }

    /// <summary>A forfait ignores the legacy deduction too: the forfait is the whole rule.</summary>
    [Fact]
    public void LetsTheForfaitOverrideTheLegacyDeduction()
    {
        Assert.Equal(180_000, Summary(unbilled: 200_000, invoiced: 120_000, legacy: 120_000, forfait: 300_000)
            .LeftToBillCents);
    }

    /// <summary>
    /// Sous-traitance changes the margin and never what the client owes: he owes the full fee whoever
    /// did the work.
    /// </summary>
    [Fact]
    public void TakesSubcontractingOffTheMarginAndNothingElse()
    {
        var summary = Summary(unbilled: 50_000, invoiced: 300_000, subcontracted: 90_000);

        Assert.Equal(210_000, summary.NetCents);
        Assert.Equal(50_000, summary.LeftToBillCents);
        Assert.Equal(300_000, summary.InvoicedCents);
    }

    [Fact]
    public void AddsPaidFacturesAndProvisionsIntoWhatCameIn()
    {
        var summary = Summary(invoiced: 300_000, collected: 300_000 + 50_000);

        Assert.Equal(350_000, summary.CollectedCents);
        Assert.Equal(-50_000, summary.OwedCents);
    }
}
