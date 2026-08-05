using Avocado.Server.Data;
using Avocado.Server.Features.Activities.Endpoints.Dtos;
using Avocado.Server.Features.Contacts.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Activities.Endpoints;

public static class ListActivities
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        AvocadoDbContext database,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = database.Activities
            .AsNoTracking()
            .Where(activity => activity.MatterId == matterId);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(activity => activity.OccurredAt)
            .Skip(skip)
            .Take(Math.Clamp(take, 1, 200))
            .Select(activity => new ActivityListItem(
                activity.Id,
                activity.Type,
                activity.OccurredAt,
                activity.ContactId,
                activity.Contact == null
                    ? null
                    : activity.Contact.Type == ContactType.Organisation
                        ? activity.Contact.LegalName
                        : (activity.Contact.FirstName + " " + activity.Contact.LastName).Trim(),
                activity.Subject,
                activity.Body,
                activity.TrackingNumber,
                database.TimeEntries
                    .Where(entry => entry.ActivityId == activity.Id)
                    .Sum(entry => (int?)entry.DurationMinutes),
                database.Documents
                    .Where(document => document.ActivityId == activity.Id)
                    .Select(document => new ActivityAttachment(
                        document.Id, document.FileName, document.SizeBytes, document.ExhibitNumber))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ActivityListPage(items, total));
    }
}
