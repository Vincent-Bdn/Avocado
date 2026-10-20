using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Dashboards;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;

namespace Avocado.Server.Tests.Dashboards;

/// <summary>
/// « Pouvoir sélectionner une période donnée et faire apparaître les données factu : montant
/// facturé, encaissé, travaillé. Ex : sur la période du 1er janvier au 12 mars j'ai facturé xx. »
///
/// <para>A period like that starts and ends mid-month, so the buckets are months while the rows are
/// filtered on the exact dates. What these hold is that the partial buckets hold only the part of the
/// month that is inside the period, and that the totals foot to the buckets: a card that disagrees
/// with the chart above it is worse than no card.</para>
/// </summary>
public sealed class HonorairesPeriodTests : IDisposable
{
    private readonly TestVault _vault = new();

    /// <summary>200 €/h, so an hour is 20 000 centimes.</summary>
    private Matter Dossier(string name = "PHOENIX") => _vault.Save(new Matter
    {
        Reference = "2026-0001",
        Name = name,
        OpenedOn = new DateOnly(2026, 1, 2),
        HourlyRateCents = 20_000,
    });

    private void Facture(Matter matter, DateOnly date, long cents, bool paid = false, bool historical = false) =>
        _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = date,
            AmountExclVatCents = cents,
            IsPaid = paid,
            PaidOn = paid ? date : null,
            IsHistorical = historical,
        });

    private void Heures(Matter matter, DateOnly date, int minutes) =>
        _vault.Save(new TimeEntry
        {
            MatterId = matter.Id,
            Date = date,
            DurationMinutes = minutes,
            Task = "Rédaction",
            IsBillable = true,
        });

    private Task<Features.Dashboards.ValueObjects.DashboardHonoraires> Period(DateOnly from, DateOnly to) =>
        HonorairesQuery.ForPeriodAsync(_vault.Database, from, to, default);

    [Fact]
    public async Task CountsOnlyWhatIsInsideThePeriod()
    {
        var matter = Dossier();

        Facture(matter, new DateOnly(2025, 12, 31), 100_000);
        Facture(matter, new DateOnly(2026, 1, 15), 200_000);
        Facture(matter, new DateOnly(2026, 3, 12), 300_000);
        Facture(matter, new DateOnly(2026, 3, 13), 400_000);

        var honoraires = await Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 12));

        // The 12th is in: « du 1er janvier au 12 mars » includes the 12th. The 13th is not, and
        // neither is 31 December.
        Assert.Equal(500_000, honoraires.InvoicedCents);
    }

    /// <summary>
    /// A period that starts on the 12th must not pull in the first eleven days of that month through
    /// its own bucket, which is the mistake a month-granular query makes without noticing.
    /// </summary>
    [Fact]
    public async Task ClampsThePartialFirstAndLastBuckets()
    {
        var matter = Dossier();

        Facture(matter, new DateOnly(2026, 1, 5), 100_000);
        Facture(matter, new DateOnly(2026, 1, 20), 200_000);
        Facture(matter, new DateOnly(2026, 2, 3), 50_000);
        Facture(matter, new DateOnly(2026, 2, 25), 900_000);

        var honoraires = await Period(new DateOnly(2026, 1, 12), new DateOnly(2026, 2, 10));

        Assert.Equal(2, honoraires.Months.Count);
        Assert.Equal(200_000, honoraires.Months[0].InvoicedCents);
        Assert.Equal(50_000, honoraires.Months[1].InvoicedCents);
        Assert.Equal(250_000, honoraires.InvoicedCents);
    }

    [Fact]
    public async Task TotalsAlwaysFootToTheBuckets()
    {
        var matter = Dossier();

        Facture(matter, new DateOnly(2026, 1, 10), 100_000, paid: true);
        Facture(matter, new DateOnly(2026, 2, 10), 200_000);
        Facture(matter, new DateOnly(2026, 2, 20), 70_000, historical: true);
        Heures(matter, new DateOnly(2026, 1, 11), 120);
        Heures(matter, new DateOnly(2026, 3, 1), 60);

        var honoraires = await Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        Assert.Equal(honoraires.Months.Sum(month => month.InvoicedCents), honoraires.InvoicedCents);
        Assert.Equal(honoraires.Months.Sum(month => month.BillableCents), honoraires.BillableCents);
        Assert.Equal(honoraires.Months.Sum(month => month.PaidCents), honoraires.PaidCents);
        Assert.Equal(honoraires.Months.Sum(month => month.HistoricalCents), honoraires.HistoricalCents);

        // Three hours at 200 €, and the facture marked paid.
        Assert.Equal(60_000, honoraires.BillableCents);
        Assert.Equal(100_000, honoraires.PaidCents);
    }

    /// <summary>A period inside one month is one column, which is the shape the drill-down asks for.</summary>
    [Fact]
    public async Task OneMonthIsOneBucket()
    {
        var matter = Dossier();
        Facture(matter, new DateOnly(2026, 9, 14), 1_098_500);

        var honoraires = await Period(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Single(honoraires.Months);
        Assert.Equal(1_098_500, honoraires.Months[0].InvoicedCents);
    }

    [Fact]
    public async Task AnEmptyPeriodStillDrawsAnAxis()
    {
        Dossier();

        var honoraires = await Period(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        Assert.Equal(0, honoraires.InvoicedCents);
        Assert.True(honoraires.ScaleCents > 0, "An empty chart would otherwise divide by zero.");
    }

    // ─── Which dossiers a column is made of ───────────────────────────────────────────────────────

    [Fact]
    public async Task NamesTheDossiersBehindAColumnWorstPayerFirst()
    {
        var paid = Dossier("RÉGLÉ");
        var owing = _vault.Save(new Matter
        {
            Reference = "2026-0002",
            Name = "DOIT ENCORE",
            OpenedOn = new DateOnly(2026, 1, 2),
            HourlyRateCents = 20_000,
        });

        Facture(paid, new DateOnly(2026, 9, 3), 978_500, paid: true);
        Facture(owing, new DateOnly(2026, 9, 9), 120_000);

        var matters = await HonorairesDetailQuery.ForPeriodAsync(
            _vault.Database, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        // Her question, in her order: « parmi ces 10 985 €, non payé 1 200 € : à quels dossiers se
        // rapportent ces 1 200 € ? Permet de savoir qui relancer. »
        Assert.Equal(2, matters.Count);
        Assert.Equal("DOIT ENCORE", matters[0].Name);
        Assert.Equal(120_000, matters[0].UnpaidCents);
        Assert.Equal(0, matters[1].UnpaidCents);

        // And the rows add up to the column they were opened from.
        var column = await Period(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(column.InvoicedCents, matters.Sum(matter => matter.InvoicedCents));
        Assert.Equal(column.UnpaidCents, matters.Sum(matter => matter.UnpaidCents));
    }

    /// <summary>
    /// A dossier with hours and no facture in the period is listed: it is what the écart is made of,
    /// and it is the other half of what the chart shows.
    /// </summary>
    [Fact]
    public async Task ListsADossierWorkedAndNotBilled()
    {
        var matter = Dossier();
        Heures(matter, new DateOnly(2026, 9, 4), 180);

        var matters = await HonorairesDetailQuery.ForPeriodAsync(
            _vault.Database, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        Assert.Equal(60_000, Assert.Single(matters).BillableCents);
        Assert.Equal(0, matters[0].InvoicedCents);
    }

    /// <summary>An abandon is not turnover and appears in no figure that counts money coming in.</summary>
    [Fact]
    public async Task LeavesAbandonsOutOfTheBreakdown()
    {
        var matter = Dossier();

        _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 9, 10),
            AmountExclVatCents = 0,
            BilledTimeCents = 150_000,
            IsAbandon = true,
        });

        var matters = await HonorairesDetailQuery.ForPeriodAsync(
            _vault.Database, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        Assert.Empty(matters);
    }

    public void Dispose() => _vault.Dispose();
}
