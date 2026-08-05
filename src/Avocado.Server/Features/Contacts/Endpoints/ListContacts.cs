using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts.Endpoints;

public static class ListContacts
{
    private const int MaxResults = 200;

    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = database.Contacts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(contact =>
                EF.Functions.Like(contact.LastName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.FirstName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.LegalName ?? string.Empty, pattern) ||
                EF.Functions.Like(contact.Email ?? string.Empty, pattern));
        }

        var contacts = await query
            .OrderBy(contact => contact.LastName ?? contact.LegalName)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        return Results.Ok(contacts.Select(ContactSummary.From));
    }
}
