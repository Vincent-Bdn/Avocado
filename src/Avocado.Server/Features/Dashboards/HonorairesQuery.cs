using Avocado.Server.Data;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Dashboards;

/// <summary>
/// The twelve months behind « Honoraires facturables et facturés ».
///
/// <para>Two figures per month, and the gap between them is the information: the time recorded that
/// month valued at the dossier's rate, against the factures actually issued that month. A practice
/// that works more than it invoices sees it here before it shows up in the bank.</para>
///
/// <para>The window ends on the current month, which is in progress, its gap is expected, and the
/// expanded view says so rather than letting it read as a miss.</para>
/// </summary>
public static class HonorairesQuery
{
    private const int MonthCount = 12;

    public static async Task<DashboardHonoraires> ForPracticeAsync(
        AvocadoDbContext database,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(MonthCount - 1));
        var end = firstMonth.AddMonths(MonthCount);

        // Materialised rather than grouped in SQL: the per-entry rate override makes the value a
        // row-level expression, and a year of a solo practice is hundreds of rows, not millions.
        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.IsBillable && entry.Date >= firstMonth && entry.Date < end)
            .Select(entry => new
            {
                entry.Date,
                entry.DurationMinutes,
                Rate = entry.HourlyRateCentsOverride ?? entry.Matter!.HourlyRateCents,
            })
            .ToListAsync(cancellationToken);

        var costs = await database.Costs
            .AsNoTracking()
            .Where(cost => cost.Date >= firstMonth && cost.Date < end)
            .Select(cost => new { cost.Date, cost.AmountExclVatCents })
            .ToListAsync(cancellationToken);

        var invoices = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Date >= firstMonth && invoice.Date < end)
            .Select(invoice => new { invoice.Date, invoice.AmountExclVatCents, invoice.IsPaid })
            .ToListAsync(cancellationToken);

        var months = new List<HonoraireMonth>(MonthCount);

        for (var index = 0; index < MonthCount; index++)
        {
            var month = firstMonth.AddMonths(index);
            var next = month.AddMonths(1);

            var billable = entries
                .Where(entry => entry.Date >= month && entry.Date < next)
                .Sum(entry => entry.Rate * entry.DurationMinutes / 60);

            var issued = invoices.Where(invoice => invoice.Date >= month && invoice.Date < next).ToList();

            months.Add(new HonoraireMonth(
                month,
                billable,
                issued.Sum(invoice => invoice.AmountExclVatCents),
                issued.Where(invoice => invoice.IsPaid).Sum(invoice => invoice.AmountExclVatCents),
                costs.Where(cost => cost.Date >= month && cost.Date < next)
                    .Sum(cost => cost.AmountExclVatCents)));
        }

        return new DashboardHonoraires(
            months,
            months.Sum(month => month.BillableCents),
            months.Sum(month => month.InvoicedCents),
            months.Sum(month => month.PaidCents),
            months.Sum(month => month.SubcontractedCents),
            Scale(months));
    }

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
