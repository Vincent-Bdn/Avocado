using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Data;

/// <summary>
/// The one place tenancy is resolved. Slice code asks for a context by vault id and never learns
/// whether it is running on a desktop with a single vault or a server with a thousand.
/// </summary>
public sealed class VaultDbContextFactory(IVaultStore vaultStore)
{
    public AvocadoDbContext Create(Guid vaultId)
    {
        var vault = vaultStore.Get(vaultId);

        // Already keyed by VaultDatabase.Open, which also asserts SQLCipher is genuinely active.
        // `contextOwnsConnection: true` matters: connection pooling is off, so every context holds a
        // real handle and leaking them would exhaust file descriptors under load.
        var connection = vault.OpenConnection();

        var options = new DbContextOptionsBuilder<AvocadoDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options;

        return new AvocadoDbContext(options);
    }
}

/// <summary>
/// Which vault the current request belongs to. Constant on the desktop; resolved from authentication
/// once there is a hosted version.
/// </summary>
public sealed class TenantContext(Guid vaultId)
{
    public Guid VaultId { get; } = vaultId;
}
