using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Data;

/// <summary>
/// Brings a vault's schema up to date, taking a snapshot first.
/// <para>
/// SQLite DDL is transactional, so a migration that <em>fails</em> rolls itself back. The dangerous
/// case is a migration that succeeds and is wrong — dropping a column, mangling a conversion — which
/// nothing can undo. This is the user's only copy of their practice, so the snapshot is mandatory,
/// not a nicety.
/// </para>
/// </summary>
public static class VaultMigrator
{
    public static async Task<MigrationOutcome> EnsureUpToDateAsync(
        OpenVault vault,
        VaultDbContextFactory contextFactory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory.Create(vault.Id);

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Vault {VaultId} schema is up to date.", vault.Id);
            return new MigrationOutcome(false, [], null);
        }

        logger.LogInformation(
            "Vault {VaultId} has {Count} pending migration(s): {Migrations}. Taking a snapshot first.",
            vault.Id, pending.Count, string.Join(", ", pending));

        var backupPath = vault.CreateBackup("pre-migration");
        logger.LogInformation("Snapshot written to {BackupPath}.", backupPath);

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Say where the good copy is, in the message the user will actually see.
            throw new VaultException(
                $"Migrating vault {vault.Id} failed. The database from before the attempt is intact at " +
                $"'{backupPath}'. Do not delete it.",
                exception);
        }

        logger.LogInformation("Vault {VaultId} migrated.", vault.Id);
        return new MigrationOutcome(true, pending, backupPath);
    }
}

public sealed record MigrationOutcome(bool Migrated, IReadOnlyList<string> Applied, string? BackupPath);
