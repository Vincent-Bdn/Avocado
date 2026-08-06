using Avocado.Server.Features.Vaults;
using Avocado.Server.Features.Vaults.Enums;

namespace Avocado.Server.Hosting;

/// <summary>
/// Refuses every request that needs data while the vault is shut, and says which state it is in so
/// the renderer can show the wizard or ask for the recovery key.
/// <para>
/// A guard rather than an exception filter: without it, the first request would reach a DbContext
/// built on a vault that is not open and fail as a 500 somewhere deep in EF, which tells the user
/// nothing and the developer little more.
/// </para>
/// </summary>
public sealed class VaultReadyMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Reachable with the vault shut. Must stay in step with <c>VaultEndpoints</c> — a route added
    /// there and forgotten here is unreachable exactly when it is needed.
    /// </summary>
    private static readonly string[] AlwaysAllowed = ["/api/vault", "/health"];

    public async Task InvokeAsync(HttpContext context, VaultSession session)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        var allowed =
            session.State == VaultState.Unlocked ||
            AlwaysAllowed.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            await Results.Problem(
                    title: session.State == VaultState.Absent ? "Aucun coffre" : "Coffre verrouillé",
                    detail: session.LockReason
                            ?? "Le coffre n'est pas ouvert. Terminez la configuration pour continuer.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["vaultState"] = session.State.ToString() })
                .ExecuteAsync(context);

            return;
        }

        await next(context);
    }
}
