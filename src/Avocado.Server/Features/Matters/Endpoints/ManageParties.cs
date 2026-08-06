using Avocado.Server.Data;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

/// <summary>
/// Attaching a tiers to a dossier and giving them a role. The role is free text on purpose, so the
/// application never has to be taught what a « sapiteur » is.
/// </summary>
public static class ManageParties
{
    public static async Task<IResult> AddAsync(
        Guid matterId,
        MatterPartyInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["party"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (!await database.Contacts.AnyAsync(contact => contact.Id == input.ContactId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contactId"] = ["Ce tiers n'existe pas."],
            });
        }

        var already = await database.MatterParties.AnyAsync(
            party => party.MatterId == matterId && party.ContactId == input.ContactId,
            cancellationToken);

        if (already)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contactId"] = ["Ce tiers est déjà partie à ce dossier."],
            });
        }

        var party = new MatterParty
        {
            MatterId = matterId,
            ContactId = input.ContactId,
            IsClient = input.IsClient,
            Role = Trimmed(input.Role),
        };

        database.MatterParties.Add(party);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/parties/{party.Id}", new { party.Id });
    }

    public static async Task<IResult> UpdateAsync(
        Guid id,
        MatterPartyInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["party"] = [error] });
        }

        var party = await database.MatterParties
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (party is null)
        {
            return Results.NotFound();
        }

        party.ContactId = input.ContactId;
        party.IsClient = input.IsClient;
        party.Role = Trimmed(input.Role);

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Removing the client is refused. A dossier without a client cannot be billed and cannot be
    /// listed, so the way to change client is to add the new one first.
    /// </summary>
    public static async Task<IResult> RemoveAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var party = await database.MatterParties
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (party is null)
        {
            return Results.NoContent();
        }

        if (party.IsClient)
        {
            var otherClients = await database.MatterParties.CountAsync(
                other => other.MatterId == party.MatterId && other.IsClient && other.Id != id,
                cancellationToken);

            if (otherClients == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["party"] = ["Un dossier doit garder un client. Ajoutez le nouveau avant de retirer celui-ci."],
                });
            }
        }

        database.MatterParties.Remove(party);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
