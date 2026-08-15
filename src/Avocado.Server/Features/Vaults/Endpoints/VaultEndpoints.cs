namespace Avocado.Server.Features.Vaults.Endpoints;

public static class VaultEndpoints
{
    /// <summary>
    /// The only routes reachable before the vault is open, see <c>VaultReadyMiddleware</c>, which
    /// holds the matching list.
    /// </summary>
    public static IEndpointRouteBuilder MapVault(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/vault").WithTags("Vault");

        group.MapGet("/status", GetVaultStatus.Handle);

        // Prepare validates and generates keys in memory; commit is the first write to disk.
        group.MapPost("/prepare", CreateVault.Prepare);
        group.MapPost("/discard", CreateVault.Discard);
        group.MapPost("/commit", CreateVault.CommitAsync);

        group.MapPost("/unlock", UnlockVault.HandleAsync);

        // The other first run: this machine is the replacement, and everything comes back from a
        // destination plus the recovery key.
        group.MapPost("/restore/discover", RestoreVault.DiscoverAsync);
        group.MapPost("/restore/recovery-file", RestoreVault.ReadRecoveryFile);
        group.MapPost("/restore", RestoreVault.HandleAsync);

        return routes;
    }
}
