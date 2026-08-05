using Avocado.Server.Data;
using Avocado.Server.Features.TimeEntries.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.TimeEntries.Endpoints;

public static class UpdateTimeEntry
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        TimeEntryInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeEntry"] = [error] });
        }

        var entry = await database.TimeEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        entry.Date = input.Date;
        entry.StartedAt = input.StartedAt;
        entry.Task = input.Task.Trim();
        entry.DurationMinutes = input.DurationMinutes;
        entry.IsBillable = input.IsBillable;
        entry.HourlyRateCentsOverride = input.HourlyRateCentsOverride;

        // The link to its journal entry is deliberately left alone: it records where the time came
        // from, and correcting a duration does not change that.
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
