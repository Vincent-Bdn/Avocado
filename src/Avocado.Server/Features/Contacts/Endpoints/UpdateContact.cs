using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts.Endpoints;

public static class UpdateContact
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ContactInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [error] });
        }

        var contact = await database.Contacts
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (contact is null)
        {
            return Results.NotFound();
        }

        input.ApplyTo(contact);
        contact.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(contact);
    }
}
