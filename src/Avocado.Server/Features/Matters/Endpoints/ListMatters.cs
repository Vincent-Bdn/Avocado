using Avocado.Server.Data;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Avocado.Server.Features.Matters.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class ListMatters
{
    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        string? search,
        MatterStatusFilter status = MatterStatusFilter.Open,
        [FromQuery(Name = "deadline")] MatterDeadlineFilter[]? deadlineFilters = null,
        Guid? clientId = null,
        MatterSort sort = MatterSort.NextDeadline,
        bool descending = false,
        int skip = 0,
        int take = 40,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var query = database.Matters.AsNoTracking();

        query = status switch
        {
            MatterStatusFilter.Open => query.Where(matter => matter.ClosedOn == null),
            MatterStatusFilter.Closed => query.Where(matter => matter.ClosedOn != null),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            // The scope the empty state promises: nom, référence, client, n° RG, description.
            var pattern = $"%{search.Trim()}%";
            query = query.Where(matter =>
                EF.Functions.Like(matter.Name, pattern) ||
                EF.Functions.Like(matter.Reference, pattern) ||
                EF.Functions.Like(matter.CourtCaseNumber ?? string.Empty, pattern) ||
                EF.Functions.Like(matter.Description ?? string.Empty, pattern) ||
                matter.Parties.Any(party =>
                    EF.Functions.Like(party.Contact!.LegalName ?? string.Empty, pattern) ||
                    EF.Functions.Like(party.Contact!.LastName ?? string.Empty, pattern)));
        }

        if (clientId is { } client)
        {
            query = query.Where(matter =>
                matter.Parties.Any(party => party.IsClient && party.ContactId == client));
        }

        query = ApplyDeadlineFilters(query, database, today, deadlineFilters);

        var total = await query.CountAsync(cancellationToken);

        // Order and page on the entity query, then project. EF cannot translate an ORDER BY over a
        // record constructed in a Select, and the page is only 40 rows, so the per-row subqueries run
        // for those 40 rather than for all 418.
        var projected = Order(query, database, sort, descending)
            .Skip(skip)
            .Take(Math.Clamp(take, 1, 200))
            .Select(matter => new MatterListItem(
            matter.Id,
            matter.Reference,
            matter.Name,
            matter.Parties
                .Where(party => party.IsClient)
                .OrderBy(party => party.Id)
                .Select(party => party.Contact!.Type == Contacts.Enums.ContactType.Organisation
                    ? party.Contact!.LegalName
                    : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
                .FirstOrDefault(),
            matter.CourtCaseNumber,
            matter.ClosedOn == null,
            matter.ClosedOn == null
                ? database.Deadlines
                    .Where(deadline => deadline.MatterId == matter.Id && !deadline.IsDone)
                    .OrderBy(deadline => deadline.Date)
                    .Select(deadline => (DateOnly?)deadline.Date)
                    .FirstOrDefault()
                : null,
            matter.ClosedOn == null
                ? database.Deadlines
                    .Where(deadline => deadline.MatterId == matter.Id && !deadline.IsDone)
                    .OrderBy(deadline => deadline.Date)
                    .Select(deadline => deadline.Time)
                    .FirstOrDefault()
                : null,
            null,
            database.Activities
                .Where(activity => activity.MatterId == matter.Id)
                .OrderByDescending(activity => activity.OccurredAt)
                .Select(activity => (DateTimeOffset?)activity.OccurredAt)
                .FirstOrDefault()));

        var items = await projected.ToListAsync(cancellationToken);

        // Urgency is a domain rule, not a SQL expression — applied once the rows are in memory.
        var withUrgency = items
            .Select(item => item with
            {
                NextDeadlineUrgency = item.NextDeadlineDate is { } date
                    ? DeadlineUrgencyRule.For(date, today)
                    : null,
            })
            .ToList();

        return Results.Ok(new MatterListPage(withUrgency, total));
    }

    private static IQueryable<Matter> ApplyDeadlineFilters(
        IQueryable<Matter> query,
        AvocadoDbContext database,
        DateOnly today,
        MatterDeadlineFilter[]? filters)
    {
        if (filters is null || filters.Length == 0)
        {
            return query;
        }

        // Checked boxes are a union: "dépassée OR dans les 7 jours" widens, it does not intersect.
        var wanted = filters.Distinct().ToArray();

        return query.Where(matter =>
            (wanted.Contains(MatterDeadlineFilter.None) &&
             !database.Deadlines.Any(d => d.MatterId == matter.Id && !d.IsDone)) ||
            (wanted.Contains(MatterDeadlineFilter.Overdue) &&
             database.Deadlines.Any(d => d.MatterId == matter.Id && !d.IsDone && d.Date < today)) ||
            (wanted.Contains(MatterDeadlineFilter.WithinSevenDays) &&
             database.Deadlines.Any(d => d.MatterId == matter.Id && !d.IsDone &&
                                         d.Date >= today && d.Date <= today.AddDays(7))) ||
            (wanted.Contains(MatterDeadlineFilter.WithinThirtyDays) &&
             database.Deadlines.Any(d => d.MatterId == matter.Id && !d.IsDone &&
                                         d.Date >= today && d.Date <= today.AddDays(30))));
    }

    private static IQueryable<Matter> Order(
        IQueryable<Matter> query,
        AvocadoDbContext database,
        MatterSort sort,
        bool descending) => (sort, descending) switch
    {
        (MatterSort.Reference, false) => query.OrderBy(matter => matter.Reference),
        (MatterSort.Reference, true) => query.OrderByDescending(matter => matter.Reference),

        (MatterSort.Name, false) => query.OrderBy(matter => matter.Name),
        (MatterSort.Name, true) => query.OrderByDescending(matter => matter.Name),

        (MatterSort.Client, false) => query.OrderBy(matter => matter.Parties
            .Where(party => party.IsClient)
            .OrderBy(party => party.Id)
            .Select(party => party.Contact!.LegalName ?? party.Contact!.LastName)
            .FirstOrDefault()),
        (MatterSort.Client, true) => query.OrderByDescending(matter => matter.Parties
            .Where(party => party.IsClient)
            .OrderBy(party => party.Id)
            .Select(party => party.Contact!.LegalName ?? party.Contact!.LastName)
            .FirstOrDefault()),

        (MatterSort.LastActivity, false) => query.OrderBy(matter => database.Activities
            .Where(activity => activity.MatterId == matter.Id)
            .Max(activity => (DateTimeOffset?)activity.OccurredAt)),
        (MatterSort.LastActivity, true) => query.OrderByDescending(matter => database.Activities
            .Where(activity => activity.MatterId == matter.Id)
            .Max(activity => (DateTimeOffset?)activity.OccurredAt)),

        // Default. The subqueries are repeated inline rather than factored into a helper: EF cannot
        // see through a method call and fails to translate the ORDER BY.
        //
        // Matters with no deadline sort last either way — a blank cell is not urgent, and SQLite
        // orders NULLs first, which would otherwise bury the overdue rows underneath them.
        (_, false) => query
            .OrderBy(matter => !database.Deadlines
                .Any(deadline => deadline.MatterId == matter.Id && !deadline.IsDone))
            .ThenBy(matter => database.Deadlines
                .Where(deadline => deadline.MatterId == matter.Id && !deadline.IsDone)
                .Min(deadline => (DateOnly?)deadline.Date)),
        (_, true) => query
            .OrderBy(matter => !database.Deadlines
                .Any(deadline => deadline.MatterId == matter.Id && !deadline.IsDone))
            .ThenByDescending(matter => database.Deadlines
                .Where(deadline => deadline.MatterId == matter.Id && !deadline.IsDone)
                .Min(deadline => (DateOnly?)deadline.Date)),
    };
}
