namespace Avocado.Server.Features.Contacts.Endpoints;

/// <summary>Routing only. Each handler lives in its own file beside this one.</summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/contacts").WithTags("Contacts");

        group.MapGet("/", ListContacts.HandleAsync);
        group.MapGet("/{id:guid}", GetContact.HandleAsync);
        group.MapPost("/", CreateContact.HandleAsync);
        group.MapPut("/{id:guid}", UpdateContact.HandleAsync);
        group.MapDelete("/{id:guid}", DeleteContact.HandleAsync);

        return routes;
    }
}
