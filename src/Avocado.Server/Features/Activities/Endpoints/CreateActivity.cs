using Avocado.Server.Data;
using Avocado.Server.Features.Activities.Endpoints.Dtos;
using Avocado.Server.Features.TimeEntries;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Activities.Endpoints;

public static class CreateActivity
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        ActivityInput input,
        AvocadoDbContext database,
        CurrentUser currentUser,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["activity"] = [error] });
        }

        var matter = await database.Matters
            .Where(candidate => candidate.Id == matterId)
            .Select(candidate => new { candidate.Id, candidate.ClosedOn })
            .FirstOrDefaultAsync(cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        // The Clôturé screen replaces the composer rather than disabling it, so this is a state the UI
        // does not offer — but the API is also reachable from ⌘K, and a frozen journal must stay frozen.
        if (matter.ClosedOn is not null)
        {
            return Results.Problem(
                title: "Dossier clôturé",
                detail: "Le journal de ce dossier est figé. Réouvrez le dossier pour y écrire.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var occurredAt = input.OccurredAt ?? clock.GetLocalNow();
        var author = await currentUser.GetAsync(cancellationToken);

        var activity = new Activity
        {
            MatterId = matterId,
            OccurredAt = occurredAt,
            Type = input.Type,
            ContactId = input.ContactId,
            Subject = input.Subject?.Trim(),
            Body = input.Body?.Trim(),
            TrackingNumber = string.IsNullOrWhiteSpace(input.TrackingNumber)
                ? null
                : input.TrackingNumber.Trim(),
            UserId = author.Id,
        };

        database.Activities.Add(activity);

        // One transaction, so the entry and its billable time can never end up half-recorded.
        if (input.DurationMinutes is { } minutes)
        {
            database.TimeEntries.Add(new TimeEntry
            {
                MatterId = matterId,
                ActivityId = activity.Id,
                Date = DateOnly.FromDateTime(occurredAt.DateTime),
                DurationMinutes = minutes,
                IsBillable = input.DurationIsBillable,
                UserId = author.Id,
                Task = activity.Subject ?? input.Type.ToString(),
            });
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/activities/{activity.Id}", new { activity.Id });
    }
}
