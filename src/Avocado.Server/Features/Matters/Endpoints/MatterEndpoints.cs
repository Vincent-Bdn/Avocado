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
        group.MapPost("/{id:guid}/parties", ManageParties.AddAsync);
        group.MapPut("/{id:guid}/favourite", SetFavourite.HandleAsync);

        routes.MapPut("/api/parties/{id:guid}", ManageParties.UpdateAsync).WithTags("Matters");
        routes.MapDelete("/api/parties/{id:guid}", ManageParties.RemoveAsync).WithTags("Matters");

        return routes;
    }
}
