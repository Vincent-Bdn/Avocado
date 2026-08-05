namespace Avocado.Server.Features.Searches.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/search").WithTags("Search");

        group.MapGet("/", SearchEverything.HandleAsync);
        group.MapGet("/starting-points", ListStartingPoints.HandleAsync);

        return routes;
    }
}
