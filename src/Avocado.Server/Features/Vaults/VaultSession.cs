using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault;
using Avocado.Vault.Storage;

namespace Avocado.Server.Features.Vaults;

/// <summary>
/// The vault's lifecycle for one running application: absent, locked, or open.
/// <para>
/// The server must be able to start <em>without</em> a vault. On a new machine there is nothing to
/// open yet, and the setup wizard is served over this same API, so refusing to boot would make the
/// first run unreachable. Unlocking is therefore an operation, not a startup precondition.
/// </para>
/// </summary>
public sealed class VaultSession : IVaultStore, IDisposable
{
    private readonly object _gate = new();
    private OpenVault? _vault;
    private PendingVault? _pending;

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

    /// <summary>True once <see cref="Prepare"/> has run and <see cref="Commit"/> has not.</summary>
    public bool HasPending => _pending is not null;

    /// <summary>
    /// Validates the destination and generates the keys, writing nothing. The recovery code comes back
    /// exactly once; the wizard must not let the user past it until it has been printed or saved.
    /// </summary>
    public string Prepare(string directory, bool allowSyncedFolder)
    {
        lock (_gate)
        {
            var pending = VaultManager.Prepare(directory, allowSyncedFolder: allowSyncedFolder);

            _pending?.Dispose();
            _pending = pending;

            return pending.RecoveryCode;
        }
    }

    /// <summary>Drops the generated keys. Nothing was on disk, so going back leaves no trace.</summary>
    public void DiscardPending()
    {
        lock (_gate)
        {
            _pending?.Dispose();
            _pending = null;
        }
    }

    /// <summary>Writes the prepared vault and opens it. The first moment anything exists on disk.</summary>
    public VaultCreation Commit()
    {
        lock (_gate)
        {
            var pending = _pending
                ?? throw new VaultException("Aucun coffre en attente de création.");

            var creation = VaultManager.Commit(pending);

            _pending = null;
            _vault?.Dispose();
            _vault = creation.Vault;
            Paths = creation.Vault.Paths;
            State = VaultState.Unlocked;
            LockReason = null;

            return creation;
        }
    }

    /// <summary>
    /// Takes over a vault someone else opened, which today means one just rebuilt from a backup.
    /// Restoring already produced an unlocked vault; without this the session would still believe
    /// there is none and the window would be sent back to the wizard it just came out of.
    /// </summary>
    public void Adopt(OpenVault vault)
    {
        lock (_gate)
        {
            _pending?.Dispose();
            _pending = null;

            _vault?.Dispose();
            _vault = vault;
            Paths = vault.Paths;
            State = VaultState.Unlocked;
            LockReason = null;
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
        _pending?.Dispose();
        _pending = null;
        _vault?.Dispose();
        _vault = null;
    }
}
