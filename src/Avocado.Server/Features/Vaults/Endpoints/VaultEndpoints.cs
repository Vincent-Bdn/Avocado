namespace Avocado.Server.Features.Vaults.Endpoints;

public static class VaultEndpoints
{
    /// <summary>
    /// The only routes reachable before the vault is open — see <c>VaultReadyMiddleware</c>, which
    /// holds the matching list.
    /// </summary>
    public static IEndpointRouteBuilder MapVault(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/vault").WithTags("Vault");

        group.MapGet("/status", GetVaultStatus.Handle);
        group.MapPost("/", CreateVault.HandleAsync);
        group.MapPost("/unlock", UnlockVault.HandleAsync);

        return routes;
    }
}
