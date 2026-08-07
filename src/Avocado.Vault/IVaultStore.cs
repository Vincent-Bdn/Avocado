namespace Avocado.Vault;

/// <summary>
/// The tenancy seam, and the reason a hosted Avocado would be a deployment concern rather than a
/// rewrite: the desktop app resolves one vault, a server resolves N. Same interface, same storage,
/// same migration loop.
/// <para>
/// One SQLite file per lawyer, one key per lawyer. Physical isolation means the entire class of
/// "forgot the tenant filter" bugs cannot occur, which for data under legal privilege is the failure
/// that would end the project.
/// </para>
/// </summary>
public interface IVaultStore
{
    /// <exception cref="VaultException">No such vault, or it is locked.</exception>
    OpenVault Get(Guid vaultId);

    bool TryGet(Guid vaultId, out OpenVault? vault);
}

/// <summary>
/// Desktop implementation: exactly one vault, unlocked at startup. Every id resolves to it, so slice
/// code can be written tenant-aware from day one at zero cost.
/// </summary>
public sealed class SingleVaultStore : IVaultStore, IDisposable
{
    private readonly OpenVault _vault;

    public SingleVaultStore(OpenVault vault) => _vault = vault;

    public OpenVault Vault => _vault;

    public OpenVault Get(Guid vaultId) =>
        vaultId == Guid.Empty || vaultId == _vault.Id
            ? _vault
            : throw new VaultException($"This installation holds vault {_vault.Id}, not {vaultId}.");

    public bool TryGet(Guid vaultId, out OpenVault? vault)
    {
        if (vaultId == Guid.Empty || vaultId == _vault.Id)
        {
            vault = _vault;
            return true;
        }

        vault = null;
        return false;
    }

    public void Dispose() => _vault.Dispose();
}
