using Avocado.Server.Data;
using Avocado.Server.Features.Billings.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// Computes one dossier's figures. Lives here rather than in an endpoint because the fiche, the ⌘K
/// preview and the accueil all show the same numbers, and a second implementation would eventually
/// disagree with the first. It has, twice.
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

        var rate = matter?.HourlyRateCents ?? 0;

        // Materialised rather than aggregated in SQL: the per-entry rate override makes the value a
        // row-level expression, and a matter has tens of entries, not thousands.
        var entries = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && entry.IsBillable)
            .Select(entry => new { entry.DurationMinutes, entry.HourlyRateCentsOverride, entry.InvoiceId })
            .ToListAsync(cancellationToken);

        long Value(int minutes, long? over) => (over ?? rate) * minutes / 60;

        var workedCents = entries.Sum(entry => Value(entry.DurationMinutes, entry.HourlyRateCentsOverride));

        var unbilled = entries.Where(entry => entry.InvoiceId is null).ToList();
        var unbilledMinutes = unbilled.Sum(entry => entry.DurationMinutes);
        var unbilledTimeCents = unbilled.Sum(entry => Value(entry.DurationMinutes, entry.HourlyRateCentsOverride));

        var invoices = await database.Invoices
            .Where(invoice => invoice.MatterId == matterId)
            .Select(invoice => new
            {
                invoice.AmountExclVatCents,
                invoice.BilledTimeCents,
                invoice.IsAbandon,
                invoice.IsPaid,
                invoice.DeductsWithoutLines,
            })
            .ToListAsync(cancellationToken);

        // An abandon is not turnover. It exists to take lines out of the unbilled total and to say so
        // as a mali, which it does through the same arithmetic as everything else.
        var billed = invoices.Where(invoice => !invoice.IsAbandon).ToList();

        var invoicedCents = billed.Sum(invoice => invoice.AmountExclVatCents);

        // What she billed, less what the lines she billed it for were worth. One that consumed no
        // lines has no gap to report: a reprise, and the factures that predate line consumption.
        var varianceCents = invoices
            .Where(invoice => invoice.BilledTimeCents != 0)
            .Sum(invoice => invoice.AmountExclVatCents - invoice.BilledTimeCents);

        var legacyDeductionsCents = billed
            .Where(invoice => invoice.DeductsWithoutLines)
            .Sum(invoice => invoice.AmountExclVatCents);

        var ledger = await database.LedgerEntries
            .Where(entry => entry.MatterId == matterId)
            .Select(entry => entry.AmountCents)
            .ToListAsync(cancellationToken);

        // Signed: received from the client is positive, advanced on his behalf is negative. They are
        // two different facts and no longer added together into one that means neither.
        var provisionsCents = ledger.Where(amount => amount > 0).Sum();
        var disbursementsCents = -ledger.Where(amount => amount < 0).Sum();

        var collectedCents = billed.Where(invoice => invoice.IsPaid).Sum(invoice => invoice.AmountExclVatCents)
            + provisionsCents;

        var subcontractedCents = await database.Costs
            .Where(cost => cost.MatterId == matterId)
            .SumAsync(cost => (long?)cost.AmountExclVatCents, cancellationToken) ?? 0;

        return BillingSummary.Compute(
            workedCents,
            unbilledTimeCents,
            unbilledMinutes,
            invoicedCents,
            collectedCents,
            disbursementsCents,
            varianceCents,
            subcontractedCents,
            legacyDeductionsCents,
            matter?.FixedFeeCents);
    }
}
