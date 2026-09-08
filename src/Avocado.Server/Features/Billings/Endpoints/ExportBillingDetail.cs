using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// « Détail facture »: the annexe that goes to the client with the facture.
///
/// <para>A client who receives « honoraires : 6 000 € » and nothing else asks what it covers, and the
/// answer arrives three weeks later by email. Sending the detail with the invoice is what stops that
/// conversation happening at all.</para>
///
/// <para><b>It was a spreadsheet and is now a document.</b> The reasoning for sending it was right;
/// the artefact was wrong. This page is read by a client, sometimes by a bâtonnier, occasionally by a
/// juge taxateur, and a .xlsx is not something one sends to any of them. She was rebuilding it in
/// Word by hand every time, from a layout of her own, which is what this now produces. See
/// <see cref="BillingDetailDocument"/>.</para>
/// </summary>
public static class ExportBillingDetail
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (invoice is null)
        {
            return Results.NotFound();
        }

        var matter = await database.Matters
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == invoice.MatterId, cancellationToken);

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.InvoiceId == id)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartedAt)
            .Select(entry => new
            {
                entry.Date,
                entry.DurationMinutes,
                entry.Task,
                entry.HourlyRateCentsOverride,
            })
            .ToListAsync(cancellationToken);

        var lines = entries
            .Select(entry => new DetailLine(
                entry.Date,
                entry.DurationMinutes,
                entry.Task,
                entry.HourlyRateCentsOverride ?? matter.HourlyRateCents))
            .ToList();

        var bytes = BillingDetailDocument.Build(
            invoice.ExternalReference,
            invoice.Date,
            matter.Reference,
            matter.Name,
            lines,
            invoice.DiscountBasisPoints,
            invoice.DiscountReason,
            invoice.AmountExclVatCents);

        var name = $"detail-facture-{matter.Reference}-{invoice.Date:yyyyMMdd}.docx";

        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            name);
    }
}
