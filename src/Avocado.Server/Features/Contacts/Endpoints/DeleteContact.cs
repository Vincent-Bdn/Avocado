using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts.Endpoints;

public static class DeleteContact
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var contact = await database.Contacts
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (contact is null)
        {
            return Results.NotFound();
        }

        // MatterParty restricts this delete at the database level, but a foreign-key violation surfaces
        // as an opaque 500. Answer the question she actually asked instead.
        var matterCount = await database.MatterParties
            .CountAsync(party => party.ContactId == id, cancellationToken);

        if (matterCount > 0)
        {
            return Results.Problem(
                title: "Tiers rattaché à des dossiers",
                detail: $"Ce tiers intervient dans {matterCount} dossier(s). " +
                        "Retirez-le de ces dossiers avant de le supprimer.",
                statusCode: StatusCodes.Status409Conflict);
        }

        database.Contacts.Remove(contact);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
