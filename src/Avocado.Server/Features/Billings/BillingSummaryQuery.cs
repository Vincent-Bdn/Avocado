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
        // Hours already attached to a facture are excluded, that link is what makes the figure mean
        // « depuis la dernière facture » instead of « depuis l'ouverture ».
        var billable = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && entry.IsBillable && entry.InvoiceId == null)
            .Select(entry => new { entry.DurationMinutes, entry.HourlyRateCentsOverride })
            .ToListAsync(cancellationToken);

        var billableMinutes = billable.Sum(entry => entry.DurationMinutes);
        var billableTimeCents = billable.Sum(entry =>
            (entry.HourlyRateCentsOverride ?? hourlyRateCents) * entry.DurationMinutes / 60);

        var ledgerCents = await database.LedgerEntries
            .Where(entry => entry.MatterId == matterId)
            .SumAsync(entry => (long?)entry.AmountCents, cancellationToken) ?? 0;

        var invoices = await database.Invoices
            .Where(invoice => invoice.MatterId == matterId)
            .Select(invoice => new { invoice.AmountExclVatCents, invoice.BilledTimeCents })
            .ToListAsync(cancellationToken);

        var invoicedCents = invoices.Sum(invoice => invoice.AmountExclVatCents);

        // A facture established from selected hours consumed those hours, so subtracting its amount
        // as well would count the same work twice. Only the hand-recorded ones are subtracted.
        var manualInvoicedCents = invoices
            .Where(invoice => invoice.BilledTimeCents == 0)
            .Sum(invoice => invoice.AmountExclVatCents);

        var varianceCents = invoices
            .Where(invoice => invoice.BilledTimeCents != 0)
            .Sum(invoice => invoice.AmountExclVatCents - invoice.BilledTimeCents);

        var subcontractedCents = await database.Costs
            .Where(cost => cost.MatterId == matterId)
            .SumAsync(cost => (long?)cost.AmountExclVatCents, cancellationToken) ?? 0;

        return BillingSummary.Compute(
            billableTimeCents, billableMinutes, ledgerCents, invoicedCents, manualInvoicedCents,
            varianceCents, subcontractedCents);
    }
}
