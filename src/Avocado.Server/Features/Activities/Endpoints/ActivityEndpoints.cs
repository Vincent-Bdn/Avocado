namespace Avocado.Server.Features.Activities.Endpoints;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivities(this IEndpointRouteBuilder routes)
    {
        // The journal always belongs to a matter, so reads and creates hang off it.
        routes.MapGet("/api/matters/{matterId:guid}/activities", ListActivities.HandleAsync)
            .WithTags("Activities");
        routes.MapPost("/api/matters/{matterId:guid}/activities", CreateActivity.HandleAsync)
            .WithTags("Activities");

        var group = routes.MapGroup("/api/activities").WithTags("Activities");
        group.MapPut("/{id:guid}", UpdateActivity.HandleAsync);
        group.MapDelete("/{id:guid}", DeleteActivity.HandleAsync);

        return routes;
    }
}
