using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// Corrects a mouvement. As on creation the client sends a nature and a positive amount and the sign
/// is applied here, so an edit can turn an encaissement into a débours without any client ever being
/// in a position to write the wrong sign into the ledger.
/// </summary>
public static class UpdateLedgerEntry
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        BillingLedgerInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["movement"] = [error] });
        }

        var entry = await database.LedgerEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        entry.Date = input.Date;
        entry.AmountCents = input.SignedAmountCents;
        entry.Label = input.Label.Trim();

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
