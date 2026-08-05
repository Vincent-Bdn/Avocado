using Avocado.Server.Data;
using Avocado.Server.Features.TimeEntries.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.TimeEntries.Endpoints;

public static class CreateTimeEntry
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        TimeEntryInput input,
        AvocadoDbContext database,
        CurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeEntry"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        var entry = new TimeEntry
        {
            MatterId = matterId,
            Date = input.Date,
            StartedAt = input.StartedAt,
            Task = input.Task.Trim(),
            DurationMinutes = input.DurationMinutes,
            IsBillable = input.IsBillable,
            HourlyRateCentsOverride = input.HourlyRateCentsOverride,
            UserId = (await currentUser.GetAsync(cancellationToken)).Id,
        };

        database.TimeEntries.Add(entry);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/time-entries/{entry.Id}", new { entry.Id });
    }
}
