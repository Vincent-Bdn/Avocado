namespace Avocado.Server.Features.Backups.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackups(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/backups").WithTags("Backups");

        group.MapGet("/", GetBackupStatus.HandleAsync);
        group.MapPost("/run", RunBackupNow.HandleAsync);
        group.MapGet("/volumes", DetectVolumes.HandleAsync);

        group.MapPost("/destinations", ManageDestinations.AddAsync);
        group.MapPut("/destinations/{id:guid}", ManageDestinations.UpdateAsync);
        group.MapDelete("/destinations/{id:guid}", ManageDestinations.RemoveAsync);

        return routes;
    }
}
