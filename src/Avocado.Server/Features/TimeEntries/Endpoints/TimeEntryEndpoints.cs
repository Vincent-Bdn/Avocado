namespace Avocado.Server.Features.TimeEntries.Endpoints;

public static class TimeEntryEndpoints
{
    public static IEndpointRouteBuilder MapTimeEntries(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/matters/{matterId:guid}/time-entries", ListTimeEntries.HandleAsync)
            .WithTags("TimeEntries");
        routes.MapPost("/api/matters/{matterId:guid}/time-entries", CreateTimeEntry.HandleAsync)
            .WithTags("TimeEntries");

        var group = routes.MapGroup("/api/time-entries").WithTags("TimeEntries");
        group.MapPut("/{id:guid}", UpdateTimeEntry.HandleAsync);
        group.MapDelete("/{id:guid}", DeleteTimeEntry.HandleAsync);

        return routes;
    }
}
