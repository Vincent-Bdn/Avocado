using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Avocado.Server.Features.Billings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// Saisir une facture établie ailleurs.
///
/// <para>It takes the lines it settles, like every other way a facture is created. It used to take
/// none, which meant a facture typed in by hand left its hours in « reste à facturer » for ever and
/// reported no boni or mali. See <see cref="InvoiceLines"/>.</para>
/// </summary>
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

        var matter = await database.Matters
            .AsNoTracking()
            .Where(candidate => candidate.Id == matterId)
            .Select(candidate => new { candidate.HourlyRateCents })
            .FirstOrDefaultAsync(cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var (lines, failure) = await InvoiceLines.ReadAsync(
            database, matterId, input.TimeEntryIds ?? [], cancellationToken);

        if (failure is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [InvoiceLines.Field] = [failure],
            });
        }

        var invoice = new BillingInvoice
        {
            MatterId = matterId,
            Date = input.Date,
            AmountExclVatCents = input.AmountExclVatCents,
            ExternalReference = input.ExternalReference?.Trim(),
            IsPaid = input.IsPaid,
            PaidOn = input.PaidOn,
            IsHistorical = input.IsHistorical,
            DiscountBasisPoints = input.DiscountBasisPoints,
            DiscountReason = Trimmed(input.DiscountReason),
        };

        database.Invoices.Add(invoice);

        InvoiceLines.Attach(lines, invoice, matter.HourlyRateCents);

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/invoices/{invoice.Id}",
            new { invoice.Id, invoice.BilledTimeCents, invoice.VarianceCents });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
