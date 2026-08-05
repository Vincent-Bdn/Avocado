namespace Avocado.Server.Features.Users.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", ListUsers.HandleAsync);
        group.MapPost("/", CreateUser.HandleAsync);
        group.MapPut("/{id:guid}", UpdateUser.HandleAsync);

        return routes;
    }
}
