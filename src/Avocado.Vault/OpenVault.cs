using Avocado.Vault.Blobs;
using Avocado.Vault.Crypto;
using Avocado.Vault.Keys;
using Avocado.Vault.Storage;
using Microsoft.Data.Sqlite;

namespace Avocado.Vault;

/// <summary>
/// An unlocked vault: the data encryption key is in memory, and the database and blob store are usable.
/// Disposing zeroes the key and re-locks it.
/// </summary>
public sealed class OpenVault : IDisposable
{
    private readonly SecretKey _dataKey;
    private bool _disposed;

    internal OpenVault(VaultPaths paths, VaultKeyring keyring, SecretKey dataKey)
    {
        Paths = paths;
        Keyring = keyring;
        _dataKey = dataKey;
        Blobs = new EncryptedBlobStore(paths.BlobsDirectory, dataKey);
    }

    public Guid Id => Keyring.VaultId;

    public VaultPaths Paths { get; }

    public VaultKeyring Keyring { get; }

    public IBlobStore Blobs { get; }

    /// <summary>
    /// A fresh keyed connection. The caller owns and disposes it. Connection pooling is off, so this
    /// is a genuine open each time rather than a handle that might have been keyed for another vault.
    /// </summary>
    public SqliteConnection OpenConnection(bool readOnly = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return VaultDatabase.Open(Paths.DatabaseFile, _dataKey, readOnly);
    }

    /// <summary>
    /// Issues a new recovery key, invalidating the previous one. Reachable whenever the vault opens at
    /// all, which is what keeps "I lost the printed sheet" from being fatal.
    /// </summary>
    public string RegenerateRecoveryKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Keyring.RegenerateRecoveryKey(_dataKey);
    }

    public void EnrollDeviceKey(IDeviceKeyStore deviceKeyStore)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Keyring.EnrollDeviceKey(_dataKey, deviceKeyStore);
    }

    public void SetPassphrase(string passphrase, Argon2Parameters? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Keyring.SetPassphrase(_dataKey, passphrase, parameters);
    }

    /// <summary>
    /// Confirms a recovery key still opens this vault, without unlocking anything. This is what the
    /// quarterly "please fetch your recovery sheet" prompt calls: a recovery system nobody ever tested
    /// is a recovery system that does not work.
    /// </summary>
    public bool VerifyRecoveryCode(string recoveryCode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            using var unwrapped = Keyring.UnlockWithRecoveryCode(recoveryCode);
            return unwrapped.Span.SequenceEqual(_dataKey.Span);
        }
        catch (VaultUnlockFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Snapshots the database into <c>backups/</c> and returns the path.
    /// <para>
    /// Call this before every EF Core migration. SQLite DDL is transactional so a <em>failed</em>
    /// migration rolls back on its own — but a migration that succeeds and is wrong is unrecoverable,
    /// and this is the user's only copy of their practice.
    /// </para>
    /// </summary>
    public string CreateBackup(string label = "backup")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var safeLabel = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-'));
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safeLabel}.db";
        var destination = Path.Combine(Paths.BackupsDirectory, fileName);

        using var connection = OpenConnection();
        VaultDatabase.BackupTo(connection, destination);
        return destination;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dataKey.Dispose();
    }
}
