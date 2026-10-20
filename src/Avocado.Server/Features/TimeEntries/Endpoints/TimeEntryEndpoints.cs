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

        // Days across, dossiers down. Before this, time was readable one dossier at a time and never
        // across the practice, which is the one question the saisie cannot answer about itself:
        // which afternoon did I forget to write down. See GetTimeGrid.
        group.MapGet("/grid", GetTimeGrid.HandleAsync);
        group.MapPut("/{id:guid}", UpdateTimeEntry.HandleAsync);
        group.MapDelete("/{id:guid}", DeleteTimeEntry.HandleAsync);

        return routes;
    }
}
