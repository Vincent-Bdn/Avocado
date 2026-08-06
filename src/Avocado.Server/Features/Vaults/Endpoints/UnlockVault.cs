using Avocado.Server.Data;
using Avocado.Server.Features.Vaults.Endpoints.Dtos;
using Avocado.Vault;

namespace Avocado.Server.Features.Vaults.Endpoints;

public static class UnlockVault
{
    public static async Task<IResult> HandleAsync(
        VaultUnlockRequest request,
        VaultSession session,
        VaultDbContextFactory contextFactory,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        try
        {
            session.UnlockWithRecoveryCode(request.RecoveryCode ?? string.Empty);
        }
        catch (VaultUnlockFailedException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["recoveryCode"] = [exception.Message],
            });
        }
        catch (VaultException exception)
        {
            return Results.Problem(title: "Coffre illisible", detail: exception.Message);
        }

        // A vault restored from a backup may predate this build's schema.
        await VaultMigrator.EnsureUpToDateAsync(
            session.Get(Guid.Empty), contextFactory, loggers.CreateLogger("Avocado.Vaults"), cancellationToken);

        return Results.NoContent();
    }
}
