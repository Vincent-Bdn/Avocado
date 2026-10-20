namespace Avocado.Server.Features.Dashboards.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/dashboard", GetDashboard.HandleAsync).WithTags("Dashboard");

        // La facturation sur une période, and the dossiers behind it. One endpoint serves both the
        // period selector and the drill-down, because clicking septembre on the chart is asking for
        // the period « septembre ». See GetHonoraires.
        routes.MapGet("/api/honoraires", GetHonoraires.HandleAsync).WithTags("Dashboard");
        routes.MapGet("/api/honoraires/periods", GetHonoraires.ChoicesAsync).WithTags("Dashboard");

        return routes;
    }
}
