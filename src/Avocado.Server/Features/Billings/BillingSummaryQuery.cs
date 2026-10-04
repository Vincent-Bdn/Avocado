using Avocado.Server.Data;
using Avocado.Server.Features.Billings.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// Reads one dossier's rows and hands them to <see cref="BillingSummary.From"/>.
///
/// <para>Reading and computing were one thing here, which is why the arithmetic could only be tested
/// by building a dossier. It does the reading now; what the figures mean lives on the summary, where
/// the Documentation screen's simulator reaches the very same code.</para>
/// </summary>
public static class BillingSummaryQuery
{
    public static async Task<BillingSummary> ForMatterAsync(
        AvocadoDbContext database,
        Guid matterId,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .Where(candidate => candidate.Id == matterId)
            .Select(candidate => new { candidate.HourlyRateCents, candidate.FixedFeeCents })
            .FirstOrDefaultAsync(cancellationToken);

        // Materialised rather than aggregated in SQL: the per-entry rate override makes the value a
        // row-level expression, and a matter has tens of entries, not thousands.
        var entries = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && entry.IsBillable)
            .Select(entry => new BillingTimeLine(
                entry.DurationMinutes,
                entry.HourlyRateCentsOverride,
                entry.InvoiceId != null))
            .ToListAsync(cancellationToken);

        var invoices = await database.Invoices
            .Where(invoice => invoice.MatterId == matterId)
            .Select(invoice => new BillingFacture(
                invoice.AmountExclVatCents,
                invoice.BilledTimeCents,
                invoice.IsAbandon,
                invoice.IsPaid,
                invoice.DeductsWithoutLines))
            .ToListAsync(cancellationToken);

        var ledger = await database.LedgerEntries
            .Where(entry => entry.MatterId == matterId)
            .Select(entry => entry.AmountCents)
            .ToListAsync(cancellationToken);

        var subcontractedCents = await database.Costs
            .Where(cost => cost.MatterId == matterId)
            .SumAsync(cost => (long?)cost.AmountExclVatCents, cancellationToken) ?? 0;

        return BillingSummary.From(new BillingInputs(
            matter?.HourlyRateCents ?? 0,
            matter?.FixedFeeCents,
            entries,
            invoices,
            ledger,
            subcontractedCents));
    }
}
