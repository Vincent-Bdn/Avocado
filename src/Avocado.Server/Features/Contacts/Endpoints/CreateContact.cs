using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Endpoints.Dtos;

namespace Avocado.Server.Features.Contacts.Endpoints;

public static class CreateContact
{
    public static async Task<IResult> HandleAsync(
        ContactInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [error] });
        }

        var contact = new Contact();
        input.ApplyTo(contact);

        database.Contacts.Add(contact);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/contacts/{contact.Id}", contact);
    }
}
