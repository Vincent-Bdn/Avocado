using Avocado.Server.Data;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Dashboards;

/// <summary>
/// « Honoraires facturables et facturés », over whatever period was asked for.
///
/// <para>Two figures per month, and the gap between them is the information: the time recorded that
/// month valued at the dossier's rate, against the factures actually issued that month. A practice
/// that works more than it invoices sees it here before it shows up in the bank.</para>
///
/// <para>Factures brought over from before Avocado are counted separately and drawn in their own
/// segment. They are real turnover and belong on the chart; what they cannot do is take part in the
/// comparison, since the hours behind them were never recorded here. Left in it, février 2026 read
/// 7 000 € facturé against nothing worked; left out of the chart altogether, which is where this
/// went first, the card said « facturé 0 € » for a practice that had invoiced fifty thousand.</para>
///
/// <para>Beside the gap, the boni and mali: the gap is a question of rhythm, and this one is a
/// question of price. They are deliberately not added together.</para>
///
/// <para><b>The period is a parameter.</b> It was twelve months ending on the current one, in a
/// constant, because the accueil was the only thing that asked. « Pouvoir sélectionner une période
/// donnée et faire apparaître les données factu : montant facturé, encaissé, travaillé. Ex : sur la
/// période du 1er janvier au 12 mars j'ai facturé xx. » A period like that starts and ends mid-month,
/// so the buckets are months and the <em>rows</em> are filtered on the exact dates: the first and
/// last buckets hold only the part of the month that is inside the period, and the totals are the sum
/// of the buckets, so what the card says always foots to what the chart draws.</para>
/// </summary>
public static class HonorairesQuery
{
    private const int DashboardMonths = 12;

    /// <summary>
    /// The accueil's window: twelve months, ending with the one being lived in.
    ///
    /// <para>The current month is in progress, its gap is expected, and the expanded view says so
    /// rather than letting it read as a miss.</para>
    /// </summary>
    public static Task<DashboardHonoraires> ForPracticeAsync(
        AvocadoDbContext database,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(DashboardMonths - 1));

        return ForPeriodAsync(database, first, EndOfMonth(today), cancellationToken);
    }

    /// <param name="from">Inclusive.</param>
    /// <param name="to">
    /// Inclusive, because that is how she says it: « du 1er janvier au 12 mars » includes the 12th.
    /// </param>
    public static async Task<DashboardHonoraires> ForPeriodAsync(
        AvocadoDbContext database,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        // Half-open from here down, which is the only form that does not need a « <= to » special
        // case for the last day. Every comparison below is >= start && < end.
        var end = to.AddDays(1);

        // Materialised rather than grouped in SQL: the per-entry rate override makes the value a
        // row-level expression, and a year of a solo practice is hundreds of rows, not millions.
        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.IsBillable && entry.Date >= from && entry.Date < end)
            .Select(entry => new
            {
                entry.Date,
                entry.DurationMinutes,
                Rate = entry.HourlyRateCentsOverride ?? entry.Matter!.HourlyRateCents,
            })
            .ToListAsync(cancellationToken);

        var costs = await database.Costs
            .AsNoTracking()
            .Where(cost => cost.Date >= from && cost.Date < end)
            .Select(cost => new { cost.Date, cost.AmountExclVatCents })
            .ToListAsync(cancellationToken);

        var invoices = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Date >= from && invoice.Date < end)
            .Select(invoice => new
            {
                invoice.Date,
                invoice.AmountExclVatCents,
                invoice.BilledTimeCents,
                invoice.IsPaid,
                invoice.IsHistorical,
                invoice.IsAbandon,
            })
            .ToListAsync(cancellationToken);

        var months = new List<HonoraireMonth>();

        foreach (var month in Buckets(from, to))
        {
            // Clamped to the period, so a period starting on the 12th does not pull in the first
            // eleven days of that month through the back door.
            var since = month < from ? from : month;
            var until = month.AddMonths(1) < end ? month.AddMonths(1) : end;

            var billable = entries
                .Where(entry => entry.Date >= since && entry.Date < until)
                .Sum(entry => entry.Rate * entry.DurationMinutes / 60);

            var issued = invoices.Where(invoice => invoice.Date >= since && invoice.Date < until).ToList();

            // An abandon is a facture record because it does a facture's one structural job, taking
            // lines out of the unbilled total. It is not turnover, so it is out of every bar; it is
            // an arbitrage on price, so it is in the variance.
            var billed = issued.Where(invoice => !invoice.IsAbandon).ToList();

            months.Add(new HonoraireMonth(
                month,
                billable,
                billed.Sum(invoice => invoice.AmountExclVatCents),
                billed.Where(invoice => invoice.IsHistorical).Sum(invoice => invoice.AmountExclVatCents),
                // Paid is the settled part of what is not historical, since that is the part the
                // month's own segments split.
                billed.Where(invoice => invoice.IsPaid && !invoice.IsHistorical)
                    .Sum(invoice => invoice.AmountExclVatCents),
                costs.Where(cost => cost.Date >= since && cost.Date < until)
                    .Sum(cost => cost.AmountExclVatCents),
                // Only what attached hours: a facture with none has no gap to report, which is also
                // what a reprise has.
                issued.Where(invoice => invoice.BilledTimeCents != 0)
                    .Sum(invoice => invoice.AmountExclVatCents - invoice.BilledTimeCents)));
        }

        return new DashboardHonoraires(
            months,
            months.Sum(month => month.BillableCents),
            months.Sum(month => month.InvoicedCents),
            months.Sum(month => month.HistoricalCents),
            months.Sum(month => month.PaidCents),
            months.Sum(month => month.SubcontractedCents),
            months.Sum(month => month.VarianceCents),
            Scale(months));
    }

    /// <summary>
    /// The first of each month the period touches, including the two it only touches part of.
    ///
    /// <para>A period inside one month is one bucket, which draws one column. That is the shape the
    /// drill-down uses: clicking septembre asks for septembre, and gets a card that foots to the
    /// column that was clicked.</para>
    /// </summary>
    private static IEnumerable<DateOnly> Buckets(DateOnly from, DateOnly to)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            yield return month;
        }
    }

    private static DateOnly EndOfMonth(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    /// <summary>
    /// The top of the axis: the tallest bar rounded up to a round figure, so the gridlines land on
    /// whole thousands rather than on whatever the tallest month happened to be. Never zero, an
    /// empty practice would otherwise divide by it.
    /// </summary>
    private static long Scale(IReadOnlyList<HonoraireMonth> months)
    {
        var tallest = months.Count == 0
            ? 0
            : months.Max(month => Math.Max(month.BillableCents, month.NetCents));

        if (tallest <= 0)
        {
            return 400_000; // 4 000 €, so an empty chart still draws a sensible axis.
        }

        // Four gridlines, so the step is a quarter of the top and has to be a figure someone reads
        // without thinking: the 1-2-5 ladder, which is why axes everywhere go 20, 50, 100 and never
        // 17, 34, 51.
        long[] ladder = [100_000, 200_000, 250_000, 500_000];
        var decade = 1L;

        while (true)
        {
            foreach (var rung in ladder)
            {
                var step = rung * decade;

                if (step * 4 >= tallest)
                {
                    return step * 4;
                }
            }

            decade *= 10;
        }
    }
}
