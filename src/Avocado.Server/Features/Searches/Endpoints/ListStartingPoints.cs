using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Searches.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Searches.Endpoints;

/// <summary>The palette on an empty query: the two questions of the morning.</summary>
public static class ListStartingPoints
{
    private const int RecentCount = 5;
    private const int DeadlineCount = 3;

    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Ordered and paged on the entity query, then projected: EF cannot translate an ORDER BY over
        // a record built in a Select.
        var recent = await database.Matters
            .AsNoTracking()
            .Where(matter => matter.ClosedOn == null)
            .OrderByDescending(matter => database.Activities
                .Where(activity => activity.MatterId == matter.Id)
                .Max(activity => (DateTimeOffset?)activity.OccurredAt))
            .Take(RecentCount)
            .Select(matter => new RecentMatterItem(
                matter.Id,
                matter.Reference,
                matter.Name + " · " + (matter.Parties
                    .Where(party => party.IsClient)
                    .OrderBy(party => party.Id)
                    .Select(party => party.Contact!.Type == ContactType.Organisation
                        ? party.Contact!.LegalName
                        : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
                    .FirstOrDefault() ?? string.Empty),
                database.Activities
                    .Where(activity => activity.MatterId == matter.Id)
                    .OrderByDescending(activity => activity.OccurredAt)
                    .Select(activity => (DateTimeOffset?)activity.OccurredAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var deadlines = await database.Deadlines
            .AsNoTracking()
            .Where(deadline => !deadline.IsDone && deadline.Matter!.ClosedOn == null)
            .OrderBy(deadline => deadline.Date)
            .Take(DeadlineCount)
            .Select(deadline => new NearestDeadlineItem(
                deadline.Id,
                deadline.MatterId,
                deadline.Label,
                deadline.Matter!.Name,
                deadline.Date,
                deadline.Time,
                default))
            .ToListAsync(cancellationToken);

        return Results.Ok(new SearchStartingPoints(
            recent,
            [.. deadlines.Select(deadline => deadline with
            {
                Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
            })]));
    }
}
