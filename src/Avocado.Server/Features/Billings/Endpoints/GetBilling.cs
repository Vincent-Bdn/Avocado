using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Avocado.Server.Features.Billings.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// Everything the Facturation tab draws. The three components of « reste à facturer » are returned
/// alongside the result so the subtraction can be checked by eye — a total you cannot recompute
/// yourself is never believed.
/// </summary>
public static class GetBilling
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .Where(candidate => candidate.Id == matterId)
            .Select(candidate => new { candidate.HourlyRateCents })
            .FirstOrDefaultAsync(cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var invoices = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.MatterId == matterId)
            .OrderByDescending(invoice => invoice.Date)
            .Select(invoice => new BillingInvoiceItem(
                invoice.Id,
                invoice.Date,
                invoice.ExternalReference,
                invoice.AmountExclVatCents,
                invoice.IsPaid,
                invoice.PaidOn,
                invoice.BilledTimeCents,
                invoice.AmountExclVatCents - invoice.BilledTimeCents,
                database.TimeEntries.Count(entry => entry.InvoiceId == invoice.Id)))
            .ToListAsync(cancellationToken);

        var ledger = await database.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.MatterId == matterId)
            .OrderByDescending(entry => entry.Date)
            .Select(entry => new BillingLedgerItem(
                entry.Id,
                entry.Date,
                entry.Label,
                entry.AmountCents,
                entry.AmountCents >= 0 ? BillingMovementKind.Receipt : BillingMovementKind.Disbursement))
            .ToListAsync(cancellationToken);

        var lastInvoiceDate = invoices.Count == 0 ? null : (DateOnly?)invoices.Max(i => i.Date);

        var sinceEntries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.MatterId == matterId
                            && entry.IsBillable
                            && (lastInvoiceDate == null || entry.Date > lastInvoiceDate))
            .Select(entry => new { entry.DurationMinutes, entry.HourlyRateCentsOverride })
            .ToListAsync(cancellationToken);

        var sinceLedger = ledger
            .Where(entry => lastInvoiceDate == null || entry.Date > lastInvoiceDate)
            .ToList();

        var statement = new BillingStatement(
            lastInvoiceDate,
            sinceEntries.Sum(entry => entry.DurationMinutes),
            sinceEntries.Sum(entry =>
                (entry.HourlyRateCentsOverride ?? matter.HourlyRateCents) * entry.DurationMinutes / 60),
            -sinceLedger.Where(entry => entry.AmountCents < 0).Sum(entry => entry.AmountCents),
            sinceLedger.Where(entry => entry.AmountCents > 0).Sum(entry => entry.AmountCents));

        var overview = new BillingOverview(
            await BillingSummaryQuery.ForMatterAsync(database, matterId, cancellationToken),
            invoices,
            invoices.Where(invoice => !invoice.IsPaid).Sum(invoice => invoice.AmountExclVatCents),
            ledger,
            ledger.Where(entry => entry.AmountCents > 0).Sum(entry => entry.AmountCents),
            -ledger.Where(entry => entry.AmountCents < 0).Sum(entry => entry.AmountCents),
            statement);

        return Results.Ok(overview);
    }
}
