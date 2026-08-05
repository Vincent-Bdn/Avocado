using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

public static class CreateInvoice
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        BillingInvoiceInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        var invoice = new BillingInvoice
        {
            MatterId = matterId,
            Date = input.Date,
            AmountExclVatCents = input.AmountExclVatCents,
            ExternalReference = input.ExternalReference?.Trim(),
            IsPaid = input.IsPaid,
            PaidOn = input.PaidOn,
        };

        database.Invoices.Add(invoice);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/invoices/{invoice.Id}", new { invoice.Id });
    }
}
