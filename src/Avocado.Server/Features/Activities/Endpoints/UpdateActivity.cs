using Avocado.Server.Data;
using Avocado.Server.Features.Activities.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Activities.Endpoints;

public static class UpdateActivity
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ActivityInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["activity"] = [error] });
        }

        var activity = await database.Activities
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (activity is null)
        {
            return Results.NotFound();
        }

        activity.Type = input.Type;
        activity.ContactId = input.ContactId;
        activity.Subject = input.Subject?.Trim();
        activity.Body = input.Body?.Trim();
        activity.TrackingNumber = string.IsNullOrWhiteSpace(input.TrackingNumber)
            ? null
            : input.TrackingNumber.Trim();

        if (input.OccurredAt is { } occurredAt)
        {
            activity.OccurredAt = occurredAt;
        }

        // Time attached to the entry is edited from the Temps passé tab, not from the composer —
        // silently rewriting a billable record while correcting a typo in a note would be worse than
        // making her go and change it.
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
