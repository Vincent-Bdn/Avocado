using Avocado.Server.Data;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class GetMatter
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Closing hides future échéances; it never deletes them, so reopening restores the list.
        var deadlines = matter.IsOpen
            ? await database.Deadlines
                .AsNoTracking()
                .Where(deadline => deadline.MatterId == id && !deadline.IsDone)
                .OrderBy(deadline => deadline.Date)
                .Select(deadline => new MatterDeadlineItem(
                    deadline.Id, deadline.Date, deadline.Time, deadline.Label, default))
                .ToListAsync(cancellationToken)
            : [];

        var counts = new MatterCounts(
            await database.Activities.CountAsync(activity => activity.MatterId == id, cancellationToken),
            await database.Documents.CountAsync(document => document.MatterId == id, cancellationToken),
            await database.Deadlines.CountAsync(
                deadline => deadline.MatterId == id && !deadline.IsDone, cancellationToken),
            await database.TimeEntries.CountAsync(entry => entry.MatterId == id, cancellationToken));

        var lastActivity = await database.Activities
            .AsNoTracking()
            .Where(activity => activity.MatterId == id)
            .OrderByDescending(activity => activity.OccurredAt)
            .Select(activity => new MatterLastActivity(
                activity.Type,
                activity.OccurredAt,
                activity.Subject ?? activity.Body))
            .FirstOrDefaultAsync(cancellationToken);

        var detail = new MatterDetail(
            matter.Id,
            matter.Reference,
            matter.Name,
            matter.Description,
            matter.OpenedOn,
            matter.ClosedOn,
            matter.HourlyRateCents,
            matter.CourtCaseNumber,
            matter.Classification,
            matter.Court,
            matter.IsOpen,
            matter.IsFavourite,
            [.. matter.Parties.Select(party => new MatterPartyItem(
                party.Id,
                party.ContactId,
                party.Contact!.Type,
                party.Contact.Type == ContactType.Organisation
                    ? party.Contact.LegalName ?? string.Empty
                    : $"{party.Contact.FirstName} {party.Contact.LastName}".Trim(),
                party.IsClient,
                party.Role))],
            [.. deadlines.Select(deadline => deadline with
            {
                Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
            })],
            counts,
            await BillingSummaryQuery.ForMatterAsync(database, id, cancellationToken),
            lastActivity);

        return Results.Ok(detail);
    }
}
