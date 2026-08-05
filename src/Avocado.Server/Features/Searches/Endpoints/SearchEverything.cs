using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Searches.Enums;
using Avocado.Server.Features.Searches.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Searches.Endpoints;

/// <summary>
/// ⌘K across dossiers, tiers and documents, in that fixed order. Actions are a client-side list and
/// never reach the server.
/// <para>
/// Substring matching, not fuzzy: SQLite's <c>spellfix1</c> is not compiled into the SQLCipher bundle,
/// and « vouliez-vous dire » is out of v1. What is here is exact, fast and predictable.
/// </para>
/// </summary>
public static class SearchEverything
{
    private const int PerGroup = 3;

    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        string q,
        SearchScope scope = SearchScope.All,
        bool includeClosed = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.Ok(new SearchResults([], 0));
        }

        var pattern = $"%{q.Trim()}%";
        var groups = new List<SearchResultGroup>();

        if (scope is SearchScope.All or SearchScope.Matters)
        {
            groups.Add(await MattersAsync(database, pattern, includeClosed, cancellationToken));
        }

        if (scope is SearchScope.All or SearchScope.Contacts)
        {
            groups.Add(await ContactsAsync(database, pattern, cancellationToken));
        }

        if (scope is SearchScope.All or SearchScope.Documents)
        {
            groups.Add(await DocumentsAsync(database, pattern, includeClosed, cancellationToken));
        }

        var populated = groups.Where(group => group.Total > 0).ToList();

        return Results.Ok(new SearchResults(populated, populated.Sum(group => group.Total)));
    }

    private static async Task<SearchResultGroup> MattersAsync(
        AvocadoDbContext database,
        string pattern,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var query = database.Matters.AsNoTracking();

        if (!includeClosed)
        {
            query = query.Where(matter => matter.ClosedOn == null);
        }

        query = query.Where(matter =>
            EF.Functions.Like(matter.Name, pattern) ||
            EF.Functions.Like(matter.Reference, pattern) ||
            EF.Functions.Like(matter.CourtCaseNumber ?? string.Empty, pattern) ||
            matter.Parties.Any(party =>
                EF.Functions.Like(party.Contact!.LegalName ?? string.Empty, pattern) ||
                EF.Functions.Like(party.Contact!.LastName ?? string.Empty, pattern)));

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(matter => matter.UpdatedAt)
            .Take(PerGroup)
            .Select(matter => new SearchResultItem(
                matter.Id,
                matter.Name + " · " + (matter.Parties
                    .Where(party => party.IsClient)
                    .OrderBy(party => party.Id)
                    .Select(party => party.Contact!.Type == ContactType.Organisation
                        ? party.Contact!.LegalName
                        : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
                    .FirstOrDefault() ?? string.Empty),
                matter.Reference,
                null))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup("matters", items, total);
    }

    private static async Task<SearchResultGroup> ContactsAsync(
        AvocadoDbContext database,
        string pattern,
        CancellationToken cancellationToken)
    {
        var query = database.Contacts
            .AsNoTracking()
            .Where(contact =>
                EF.Functions.Like(contact.LegalName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.LastName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.FirstName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.Email ?? string.Empty, pattern));

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(contact => contact.LastName ?? contact.LegalName)
            .Take(PerGroup)
            .Select(contact => new SearchResultItem(
                contact.Id,
                contact.Type == ContactType.Organisation
                    ? contact.LegalName ?? string.Empty
                    : (contact.FirstName + " " + contact.LastName).Trim(),
                // « rôle · N dossiers », using the role from whichever matter names one first.
                (database.MatterParties
                    .Where(party => party.ContactId == contact.Id && party.Role != null)
                    .Select(party => party.Role)
                    .FirstOrDefault() ?? "tiers")
                + " · "
                + database.MatterParties.Count(party => party.ContactId == contact.Id)
                + " dossiers",
                contact.Type))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup("contacts", items, total);
    }

    private static async Task<SearchResultGroup> DocumentsAsync(
        AvocadoDbContext database,
        string pattern,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        var query = database.Documents.AsNoTracking();

        if (!includeClosed)
        {
            query = query.Where(document => document.Matter!.ClosedOn == null);
        }

        // File name and exhibit label only. Document *contents* are not indexed in v1.
        query = query.Where(document =>
            EF.Functions.Like(document.FileName, pattern) ||
            EF.Functions.Like(document.ExhibitLabel ?? string.Empty, pattern));

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(document => document.AddedAt)
            .Take(PerGroup)
            .Select(document => new SearchResultItem(
                document.Id,
                document.ExhibitLabel ?? document.FileName,
                document.ExhibitNumber == null
                    ? "document"
                    : "pièce n° " + document.ExhibitNumber,
                null))
            .ToListAsync(cancellationToken);

        return new SearchResultGroup("documents", items, total);
    }
}
