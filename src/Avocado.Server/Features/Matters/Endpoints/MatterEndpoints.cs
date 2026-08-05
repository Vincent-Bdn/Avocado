namespace Avocado.Server.Features.Matters.Endpoints;

public static class MatterEndpoints
{
    public static IEndpointRouteBuilder MapMatters(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/matters").WithTags("Matters");

        group.MapGet("/", ListMatters.HandleAsync);
        group.MapGet("/facets", GetMatterFacets.HandleAsync);
        group.MapGet("/{id:guid}", GetMatter.HandleAsync);
        group.MapPost("/", CreateMatter.HandleAsync);
        group.MapPut("/{id:guid}", UpdateMatter.HandleAsync);
        group.MapPost("/{id:guid}/close", CloseMatter.HandleAsync);
        group.MapPost("/{id:guid}/reopen", ReopenMatter.HandleAsync);

        return routes;
    }
}
