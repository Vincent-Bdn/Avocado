using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

public static class UpdateInvoice
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        BillingInvoiceInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = [error] });
        }

        var invoice = await database.Invoices
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (invoice is null)
        {
            return Results.NotFound();
        }

        invoice.Date = input.Date;
        invoice.AmountExclVatCents = input.AmountExclVatCents;
        invoice.ExternalReference = input.ExternalReference?.Trim();
        invoice.IsPaid = input.IsPaid;
        invoice.PaidOn = input.IsPaid ? input.PaidOn : null;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
