using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts.Endpoints;

public static class GetContact
{
    private const int RecentExchangeCount = 4;

    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var contact = await database.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (contact is null)
        {
            return Results.NotFound();
        }

        var roles = await database.MatterParties
            .AsNoTracking()
            .Where(party => party.ContactId == id)
            .OrderByDescending(party => party.IsClient)
            .ThenBy(party => party.Matter!.Reference)
            .Select(party => new ContactRole(
                party.MatterId,
                party.Matter!.Reference,
                party.Matter.Name,
                party.Matter.ClosedOn == null,
                party.IsClient,
                party.Role))
            .ToListAsync(cancellationToken);

        var clientSince = await database.MatterParties
            .AsNoTracking()
            .Where(party => party.ContactId == id && party.IsClient)
            .OrderBy(party => party.Matter!.OpenedOn)
            .Select(party => (DateOnly?)party.Matter!.OpenedOn)
            .FirstOrDefaultAsync(cancellationToken);

        // Everything that happened on any of their dossiers, not only entries naming them: what she
        // wants in front of her before ringing someone back is the state of their affairs.
        var matterIds = roles.Select(role => role.MatterId).ToList();

        var exchanges = await database.Activities
            .AsNoTracking()
            .Where(activity => matterIds.Contains(activity.MatterId))
            .OrderByDescending(activity => activity.OccurredAt)
            .Take(RecentExchangeCount)
            .Select(activity => new ContactExchange(
                activity.Id,
                activity.MatterId,
                activity.Matter!.Reference,
                activity.Type,
                activity.OccurredAt,
                activity.Subject ?? activity.Body))
            .ToListAsync(cancellationToken);

        // DisplayName is a computed property EF is told to ignore, so these rows are materialised
        // first and projected in memory. A Select over it would compile and then fail in SQL.
        var attachedRows = await database.Contacts
            .AsNoTracking()
            .Where(candidate => candidate.AttachedToContactId == id)
            .OrderBy(candidate => candidate.LastName ?? candidate.LegalName)
            .ToListAsync(cancellationToken);

        var attachedPeople = attachedRows
            .Select(candidate => new ContactAttachment(
                candidate.Id,
                candidate.Type,
                candidate.DisplayName,
                candidate.AttachedAs,
                candidate.Email,
                candidate.Phone))
            .ToList();

        var parent = contact.AttachedToContactId is { } parentId
            ? await database.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == parentId, cancellationToken)
            : null;

        var attachedTo = parent is null
            ? null
            : new ContactAttachment(
                parent.Id, parent.Type, parent.DisplayName, contact.AttachedAs, parent.Email, parent.Phone);

        return Results.Ok(new ContactDetail(
            contact.Id,
            contact.Type,
            contact.DisplayName,
            contact.Civility,
            contact.LastName,
            contact.FirstName,
            contact.DateOfBirth,
            contact.LegalName,
            contact.Siren,
            contact.LegalForm,
            contact.Email,
            contact.Phone,
            contact.Address,
            contact.Notes,
            roles.Count,
            roles.Count(role => role.IsClient),
            clientSince,
            roles,
            exchanges,
            attachedPeople,
            attachedTo));
    }
}
