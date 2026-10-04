using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Dashboards;
using Avocado.Server.Features.Dashboards.Endpoints;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Avocado.Server.Tests.Dashboards;

/// <summary>
/// The accueil and the honoraires chart, against the same data as the fiche.
///
/// <para>« Reste à facturer » is computed in three places. <see cref="BillingSummaryQuery"/> already
/// carried a warning that a second implementation would eventually disagree with the first, and by
/// the time anyone looked there were three and they disagreed. A dossier repris de Gestisoft made all
/// three wrong in different ways at once, which is what these pin.</para>
/// </summary>
public class DashboardFiguresTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly Matter _matter;
    private readonly FakeTimeProvider _clock = new(new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc));

    public DashboardFiguresTests()
    {
        _matter = _vault.Save(new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2025, 1, 6),
            HourlyRateCents = 24_000,
        });
    }

    private BillingInvoice Invoice(
        DateOnly date,
        long cents,
        bool historical = false,
        long billedTimeCents = 0,
        bool abandon = false) =>
        _vault.Save(new BillingInvoice
        {
            MatterId = _matter.Id,
            Date = date,
            AmountExclVatCents = cents,
            BilledTimeCents = billedTimeCents,
            IsHistorical = historical,
            IsAbandon = abandon,
            IsPaid = true,
        });

    private void Minutes(DateOnly date, int minutes) =>
        _vault.Save(new TimeEntry
        {
            MatterId = _matter.Id,
            Date = date,
            DurationMinutes = minutes,
            Task = "Diligences",
            IsBillable = true,
        });

    private async Task<DashboardUnbilled> UnbilledAsync()
    {
        var result = await GetDashboard.HandleAsync(_vault.Database, _clock, default);

        return Assert.IsType<Ok<DashboardSummary>>(result).Value!.Unbilled;
    }

    private Task<DashboardHonoraires> ChartAsync() =>
        HonorairesQuery.ForPracticeAsync(_vault.Database, new DateOnly(2026, 8, 28), default);

    /// <summary>
    /// The accueil used to subtract every facture, historical ones included, so this dossier came out
    /// at − 8 794 € and was dropped for being negative. Not a wrong number on screen: a dossier that
    /// silently stopped appearing among those with work to bill.
    /// </summary>
    [Fact]
    public async Task KeepsADossierWhoseOnlyFacturesAreHistorical()
    {
        foreach (var amount in new long[] { 260_000, 100_000, 291_000, 128_000, 15_000, 50_000, 53_400 })
        {
            Invoice(new DateOnly(2026, 2, 27), amount, historical: true);
        }

        Minutes(new DateOnly(2026, 8, 27), 45);

        var unbilled = await UnbilledAsync();
        var row = Assert.Single(unbilled.Matters);

        Assert.Equal(18_000, row.LeftToBillCents);
        Assert.Equal(18_000, unbilled.TotalCents);
    }

    /// <summary>
    /// The number on the accueil and the number on the fiche are the same number.
    ///
    /// <para>They have disagreed twice, both times because the arithmetic was written out again
    /// rather than called. Every shape that changes the answer is here: a reprise, a facture
    /// recorded today, one carried over from before factures took their lines, and a forfait.</para>
    /// </summary>
    [Theory]
    // A reprise consumed nothing and subtracts nothing: the 300 minutes stand.
    [InlineData(true, false, null, 120_000L)]
    // Recorded today with no lines: it subtracts nothing either, because nothing was consumed.
    [InlineData(false, false, null, 120_000L)]
    // Written before factures took their lines, so it goes on subtracting.
    [InlineData(false, true, null, 60_000L)]
    // A forfait answers on its own, and the hours stop driving it.
    [InlineData(false, false, 200_000L, 140_000L)]
    public async Task TheAccueilAgreesWithTheFiche(
        bool historical,
        bool legacy,
        long? forfait,
        long expected)
    {
        Minutes(new DateOnly(2026, 8, 27), 300);

        var invoice = Invoice(new DateOnly(2026, 8, 20), 60_000, historical);
        invoice.DeductsWithoutLines = legacy;

        _matter.FixedFeeCents = forfait;
        await _vault.Database.SaveChangesAsync();

        var fiche = await BillingSummaryQuery.ForMatterAsync(_vault.Database, _matter.Id, default);
        var accueil = await UnbilledAsync();

        Assert.Equal(expected, fiche.LeftToBillCents);
        Assert.Equal(fiche.LeftToBillCents, Assert.Single(accueil.Matters).LeftToBillCents);
    }

    /// <summary>
    /// A dossier au forfait belongs on the accueil, and its hours do not.
    ///
    /// <para>The card put « 13 h » next to a 2 000 € that is a price agreed in advance: not the value
    /// of those hours, and unmoved when they change. The amount is the forfait's business, the hours
    /// are nobody's, and the row says « forfait » instead.</para>
    /// </summary>
    [Fact]
    public async Task LeavesAForfaitsHoursOutOfTheHoursBehindTheTotal()
    {
        _matter.FixedFeeCents = 300_000;

        // Long ago, so it would also land in « de plus de 60 jours » if it counted at all.
        Minutes(new DateOnly(2026, 1, 20), 780);
        Invoice(new DateOnly(2026, 9, 30), 100_000);

        var unbilled = await UnbilledAsync();
        var row = Assert.Single(unbilled.Matters);

        Assert.True(row.IsFixedFee);
        Assert.Equal(200_000, row.LeftToBillCents);
        Assert.Equal(200_000, unbilled.TotalCents);

        // The minutes are on the row, for whoever wants them; they are not behind the headline.
        Assert.Equal(780, row.BillableMinutes);
        Assert.Equal(0, unbilled.TotalBillableMinutes);
        Assert.Equal(0, unbilled.AgedOverSixtyDaysCents);
    }

    /// <summary>
    /// Hours attached to a facture left the unbilled total when it was issued. The accueil counted
    /// them anyway, which is the other way it disagreed with the fiche.
    /// </summary>
    [Fact]
    public async Task DoesNotCountHoursAlreadyAttachedToAFacture()
    {
        var invoice = new BillingInvoice
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 8, 20),
            AmountExclVatCents = 60_000,
            BilledTimeCents = 50_000,
        };

        _vault.Save(invoice);

        _vault.Save(new TimeEntry
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 8, 1),
            DurationMinutes = 125,
            Task = "Déjà facturé",
            IsBillable = true,
            InvoiceId = invoice.Id,
        });

        Minutes(new DateOnly(2026, 8, 27), 60);

        Assert.Equal(24_000, Assert.Single((await UnbilledAsync()).Matters).LeftToBillCents);
    }

    /// <summary>
    /// The chart drew February 2026 as 7 000 EUR invoiced against nothing worked, and called the gap
    /// « reste a facturer, moins 7 000 EUR ». The hours were never recorded here, so there is nothing
    /// to compare them against.
    ///
    /// <para>Taking them off the chart instead was worse, and is the other half of what this pins: the
    /// card then read « facture 0 EUR » for a practice that had invoiced fifty thousand. The figure
    /// was never the problem. The comparison was.</para>
    /// </summary>
    [Fact]
    public async Task ShowsAHistoricalFactureWithoutLettingItIntoTheComparison()
    {
        Invoice(new DateOnly(2026, 2, 27), 650_000, historical: true);
        Invoice(new DateOnly(2026, 2, 27), 50_000, historical: true);

        var chart = await ChartAsync();
        var february = Assert.Single(chart.Months, month => month.Month == new DateOnly(2026, 2, 1));

        // On the chart, and in the total.
        Assert.Equal(700_000, february.InvoicedCents);
        Assert.Equal(700_000, february.HistoricalCents);
        Assert.Equal(700_000, chart.InvoicedCents);
        Assert.Equal(700_000, chart.HistoricalCents);

        // Out of the comparison, and out of the paid/unpaid split, which is about this month.
        Assert.Equal(0, february.CurrentCents);
        Assert.Equal(0, february.GapCents);
        Assert.Equal(0, february.PaidCents);
        Assert.Equal(0, february.UnpaidCents);
    }

    /// <summary>
    /// A month holding both: the facture she issued here is compared, the one repris is shown beside
    /// it and does not move the gap.
    /// </summary>
    [Fact]
    public async Task ComparesOnlyTheCurrentPartOfAMixedMonth()
    {
        Minutes(new DateOnly(2026, 7, 6), 600);
        Invoice(new DateOnly(2026, 7, 20), 100_000);
        Invoice(new DateOnly(2026, 7, 20), 500_000, historical: true);

        var july = Assert.Single((await ChartAsync()).Months, month => month.Month == new DateOnly(2026, 7, 1));

        Assert.Equal(600_000, july.InvoicedCents);
        Assert.Equal(100_000, july.CurrentCents);
        Assert.Equal(140_000, july.GapCents);
    }

    /// <summary>
    /// The arbitrages on price, which the chart had nowhere until two lawyers asked for them by name.
    ///
    /// <para>They are not the écart and are never added to it: the écart is about when she billed,
    /// this is about at what price. A facture that attached no hours decided nothing and so counts
    /// zero, which is the same thing a reprise does.</para>
    /// </summary>
    [Fact]
    public async Task AddsUpTheArbitragesOnPrice()
    {
        // 7 200 € of hours billed 6 000 €: a mali of 1 200 € she granted.
        Invoice(new DateOnly(2026, 7, 20), 600_000, billedTimeCents: 720_000);
        // And 2 000 € of hours billed 2 500 €.
        Invoice(new DateOnly(2026, 7, 22), 250_000, billedTimeCents: 200_000);
        // Typed in with no lines behind it: nothing was decided, so it says nothing.
        Invoice(new DateOnly(2026, 8, 3), 400_000);

        var chart = await ChartAsync();
        var july = Assert.Single(chart.Months, month => month.Month == new DateOnly(2026, 7, 1));
        var august = Assert.Single(chart.Months, month => month.Month == new DateOnly(2026, 8, 1));

        Assert.Equal(-70_000, july.VarianceCents);
        Assert.Equal(0, august.VarianceCents);
        Assert.Equal(-70_000, chart.VarianceCents);
    }

    /// <summary>
    /// An abandon is a facture row because it does a facture's one structural job, taking lines out
    /// of the unbilled total. It is not turnover, so no bar may show it; it is the largest arbitrage
    /// there is, so the mali must.
    ///
    /// <para>The abandon here carries an amount, which a real one never does: it is the same row as a
    /// facture and only <see cref="BillingInvoice.IsAbandon"/> separates the two, so a figure that
    /// happens to come out right because the amount is zero is a figure keyed on the wrong column.
    /// With the flag ignored, 1 000 € of turnover reads 1 400 €.</para>
    /// </summary>
    [Fact]
    public async Task KeepsAnAbandonOutOfTurnoverAndInsideTheMali()
    {
        Invoice(new DateOnly(2026, 7, 20), 100_000, billedTimeCents: 100_000);
        Invoice(new DateOnly(2026, 7, 28), 40_000, billedTimeCents: 150_000, abandon: true);

        var july = Assert.Single((await ChartAsync()).Months, month => month.Month == new DateOnly(2026, 7, 1));

        Assert.Equal(100_000, july.InvoicedCents);
        Assert.Equal(100_000, july.PaidCents);
        Assert.Equal(-110_000, july.VarianceCents);
    }

    /// <summary>And what she actually recorded still draws the gap it should.</summary>
    [Fact]
    public async Task StillComparesWhatWasWorkedAgainstWhatWasBilled()
    {
        Minutes(new DateOnly(2026, 7, 6), 600);
        Invoice(new DateOnly(2026, 7, 20), 180_000);

        var july = Assert.Single((await ChartAsync()).Months, month => month.Month == new DateOnly(2026, 7, 1));

        Assert.Equal(240_000, july.BillableCents);
        Assert.Equal(180_000, july.InvoicedCents);
        Assert.Equal(60_000, july.GapCents);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}

/// <summary>
/// A fixed clock, so « the last twelve months » does not move under the tests. GetLocalNow is not
/// virtual, so the zone is what redirects it: UTC, and the local now is then the same instant.
/// </summary>
internal sealed class FakeTimeProvider(DateTime now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
