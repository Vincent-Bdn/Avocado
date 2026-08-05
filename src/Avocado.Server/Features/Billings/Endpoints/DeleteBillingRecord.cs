using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

public static class DeleteBillingRecord
{
    public static async Task<IResult> InvoiceAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (invoice is null)
        {
            return Results.NotFound();
        }

        database.Invoices.Remove(invoice);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    public static async Task<IResult> LedgerEntryAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var entry = await database.LedgerEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        database.LedgerEntries.Remove(entry);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
