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
        // The template names the parameter the handler takes. A mismatch does not fail at
        // startup: minimal APIs fall back to binding the value from the query string, find
        // nothing, and answer 400 with an empty body that says nothing at all.
        group.MapPost("/{matterId:guid}/parties", ManageParties.AddAsync);
        group.MapPut("/{id:guid}/favourite", SetFavourite.HandleAsync);

        routes.MapPut("/api/parties/{id:guid}", ManageParties.UpdateAsync).WithTags("Matters");
        routes.MapDelete("/api/parties/{id:guid}", ManageParties.RemoveAsync).WithTags("Matters");

        return routes;
    }
}
