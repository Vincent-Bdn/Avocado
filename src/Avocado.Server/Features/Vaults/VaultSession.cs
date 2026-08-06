using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault;
using Avocado.Vault.Storage;

namespace Avocado.Server.Features.Vaults;

/// <summary>
/// The vault's lifecycle for one running application: absent, locked, or open.
/// <para>
/// The server must be able to start <em>without</em> a vault. On a new machine there is nothing to
/// open yet, and the setup wizard is served over this same API — so refusing to boot would make the
/// first run unreachable. Unlocking is therefore an operation, not a startup precondition.
/// </para>
/// </summary>
public sealed class VaultSession : IVaultStore, IDisposable
{
    private readonly object _gate = new();
    private OpenVault? _vault;

    public VaultSession(string directory) => Paths = new VaultPaths(directory);

    public VaultPaths Paths { get; private set; }

    public VaultState State { get; private set; } = VaultState.Absent;

    /// <summary>Why it is locked, in the vault's own words. Shown to the user as-is.</summary>
    public string? LockReason { get; private set; }

    /// <summary>Opens the configured folder if it holds a vault this machine can unlock. Never throws.</summary>
    public void TryResume()
    {
        lock (_gate)
        {
            if (!Paths.Exists)
            {
                State = VaultState.Absent;
                return;
            }

            try
            {
                _vault = VaultManager.UnlockWithDeviceKey(Paths.Root);
                State = VaultState.Unlocked;
                LockReason = null;
            }
            catch (VaultException exception)
            {
                State = VaultState.Locked;
                LockReason = exception.Message;
            }
        }
    }

    /// <summary>
    /// Creates a vault and opens it. The recovery code comes back exactly once — the caller is
    /// responsible for not letting the user past it until it has been printed or saved.
    /// </summary>
    public VaultCreation Create(string directory, bool allowSyncedFolder)
    {
        lock (_gate)
        {
            var creation = VaultManager.Create(directory, allowSyncedFolder: allowSyncedFolder);

            _vault?.Dispose();
            _vault = creation.Vault;
            Paths = creation.Vault.Paths;
            State = VaultState.Unlocked;
            LockReason = null;

            return creation;
        }
    }

    /// <summary>The way back in on a replacement machine, or after restoring a folder.</summary>
    public void UnlockWithRecoveryCode(string recoveryCode)
    {
        lock (_gate)
        {
            var opened = VaultManager.UnlockWithRecoveryCode(Paths.Root, recoveryCode);

            _vault?.Dispose();
            _vault = opened;
            State = VaultState.Unlocked;
            LockReason = null;
        }
    }

    public OpenVault Get(Guid vaultId) =>
        _vault ?? throw new VaultException("Le coffre n'est pas ouvert.");

    public bool TryGet(Guid vaultId, out OpenVault? vault)
    {
        vault = _vault;
        return vault is not null;
    }

    public void Dispose()
    {
        _vault?.Dispose();
        _vault = null;
    }
}
