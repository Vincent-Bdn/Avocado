namespace Avocado.Server.Features.Dashboards.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/dashboard", GetDashboard.HandleAsync).WithTags("Dashboard");
        return routes;
    }
}
