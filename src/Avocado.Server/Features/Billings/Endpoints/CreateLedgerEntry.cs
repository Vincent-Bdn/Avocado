using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// Records an encaissement or a débours. The client sends a nature and a positive amount; the sign is
/// applied here, so no client bug can turn money advanced into money received.
/// </summary>
public static class CreateLedgerEntry
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        BillingLedgerInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["movement"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        var entry = new BillingLedgerEntry
        {
            MatterId = matterId,
            Date = input.Date,
            AmountCents = input.SignedAmountCents,
            Label = input.Label.Trim(),
        };

        database.LedgerEntries.Add(entry);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/ledger-entries/{entry.Id}", new { entry.Id, entry.AmountCents });
    }
}
