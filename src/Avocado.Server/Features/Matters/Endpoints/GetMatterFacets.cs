using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class GetMatterFacets
{
    private const int TopClientCount = 3;

    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var open = database.Matters.Where(matter => matter.ClosedOn == null);

        var openDeadlines = database.Deadlines.Where(deadline =>
            !deadline.IsDone && deadline.Matter!.ClosedOn == null);

        var clientCounts = await database.MatterParties
            .Where(party => party.IsClient && party.Matter!.ClosedOn == null)
            .GroupBy(party => party.ContactId)
            .Select(group => new
            {
                ContactId = group.Key,
                Count = group.Count(),
                Contact = group.First().Contact!,
            })
            .OrderByDescending(entry => entry.Count)
            .Take(TopClientCount + 1)
            .ToListAsync(cancellationToken);

        var distinctClients = await database.MatterParties
            .Where(party => party.IsClient && party.Matter!.ClosedOn == null)
            .Select(party => party.ContactId)
            .Distinct()
            .CountAsync(cancellationToken);

        var facets = new MatterFacets(
            await open.CountAsync(cancellationToken),
            await database.Matters.CountAsync(matter => matter.ClosedOn != null, cancellationToken),
            await openDeadlines.CountAsync(deadline => deadline.Date < today, cancellationToken),
            await openDeadlines.CountAsync(
                deadline => deadline.Date >= today && deadline.Date <= today.AddDays(7), cancellationToken),
            await openDeadlines.CountAsync(
                deadline => deadline.Date >= today && deadline.Date <= today.AddDays(30), cancellationToken),
            await open.CountAsync(
                matter => !matter.Parties.Any() || !database.Deadlines
                    .Any(deadline => deadline.MatterId == matter.Id && !deadline.IsDone),
                cancellationToken),
            [.. clientCounts.Take(TopClientCount).Select(entry => new MatterClientFacet(
                entry.ContactId,
                entry.Contact.Type == ContactType.Organisation
                    ? entry.Contact.LegalName ?? string.Empty
                    : $"{entry.Contact.FirstName} {entry.Contact.LastName}".Trim(),
                entry.Count))],
            Math.Max(0, distinctClients - TopClientCount));

        return Results.Ok(facets);
    }
}
