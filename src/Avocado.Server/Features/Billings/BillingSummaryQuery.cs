using Avocado.Server.Data;
using Avocado.Server.Features.Billings.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// Computes « reste à facturer » for one matter. Lives here rather than in an endpoint because the
/// fiche dossier's context panel, the ⌘K preview and the accueil all show the same number, and a
/// second implementation would eventually disagree with the first.
/// </summary>
public static class BillingSummaryQuery
{
    public static async Task<BillingSummary> ForMatterAsync(
        AvocadoDbContext database,
        Guid matterId,
        CancellationToken cancellationToken)
    {
        var hourlyRateCents = await database.Matters
            .Where(matter => matter.Id == matterId)
            .Select(matter => matter.HourlyRateCents)
            .FirstOrDefaultAsync(cancellationToken);

        // Materialised rather than aggregated in SQL: the per-entry rate override makes the value a
        // row-level expression, and a matter has tens of entries, not thousands.
        var billable = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && entry.IsBillable)
            .Select(entry => new { entry.DurationMinutes, entry.HourlyRateCentsOverride })
            .ToListAsync(cancellationToken);

        var billableMinutes = billable.Sum(entry => entry.DurationMinutes);
        var billableTimeCents = billable.Sum(entry =>
            (entry.HourlyRateCentsOverride ?? hourlyRateCents) * entry.DurationMinutes / 60);

        var ledgerCents = await database.LedgerEntries
            .Where(entry => entry.MatterId == matterId)
            .SumAsync(entry => (long?)entry.AmountCents, cancellationToken) ?? 0;

        var invoicedCents = await database.Invoices
            .Where(invoice => invoice.MatterId == matterId)
            .SumAsync(invoice => (long?)invoice.AmountExclVatCents, cancellationToken) ?? 0;

        return BillingSummary.Compute(billableTimeCents, billableMinutes, ledgerCents, invoicedCents);
    }
}
