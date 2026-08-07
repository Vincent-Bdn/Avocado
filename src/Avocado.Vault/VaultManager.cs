using Avocado.Vault.Crypto;
using Avocado.Vault.Keys;
using Avocado.Vault.Storage;
using Microsoft.Data.Sqlite;

namespace Avocado.Vault;

/// <param name="RecoveryCode">
/// Displayed once, then unobtainable. The setup wizard must not let the user past this without
/// printing it or writing it to a USB key, it is what makes their backups restorable.
/// </param>
public sealed record VaultCreation(OpenVault Vault, string RecoveryCode);

/// <summary>
/// A vault whose keys exist but which has not been written anywhere yet.
/// <para>
/// The wizard shows the recovery code from this, and only calls <see cref="VaultManager.Commit"/>
/// once the whole flow has been seen through. Going back therefore leaves nothing on disk to clean
/// up, and no Back button ever has to delete a folder, which is not a thing a Back button should do.
/// </para>
/// </summary>
public sealed class PendingVault(VaultPaths paths, VaultKeyringCreation keys) : IDisposable
{
    private bool _handedOver;

    public VaultPaths Paths { get; } = paths;

    internal VaultKeyringCreation Keys { get; } = keys;

    public string RecoveryCode => Keys.RecoveryCode;

    /// <summary>
    /// Called by <see cref="VaultManager.Commit"/> once the data key belongs to an open vault, so
    /// disposing this afterwards does not zero a key that is now in use.
    /// </summary>
    internal void HandOver() => _handedOver = true;

    /// <summary>Abandons the generated keys. Nothing was written, so there is nothing to remove.</summary>
    public void Dispose()
    {
        if (!_handedOver)
        {
            Keys.DataKey.Dispose();
        }
    }
}

/// <summary>Creates, unlocks and inspects vaults on disk.</summary>
public static class VaultManager
{
    /// <summary>
    /// Sets up a new vault folder: keyring, data encryption key, device key where the platform allows
    /// it, recovery key, and an empty encrypted database.
    /// </summary>
    /// <param name="allowSyncedFolder">
    /// Escape hatch for the detector's false positives. Enabling it is how databases get corrupted.
    /// </param>
    public static VaultCreation Create(
        string directory,
        IDeviceKeyStore? deviceKeyStore = null,
        bool allowSyncedFolder = false)
    {
        using var pending = Prepare(directory, deviceKeyStore, allowSyncedFolder);
        return Commit(pending);
    }

    /// <summary>
    /// Validates the destination and generates the keys, writing nothing. Every refusal the wizard has
    /// to show, synced folder, existing vault, orphaned database, happens here, before any state.
    /// </summary>
    public static PendingVault Prepare(
        string directory,
        IDeviceKeyStore? deviceKeyStore = null,
        bool allowSyncedFolder = false)
    {
        if (!allowSyncedFolder)
        {
            CloudSyncDetector.ThrowIfInsideSyncedFolder(directory);
        }

        var paths = new VaultPaths(directory);
        if (paths.Exists)
        {
            throw new VaultException($"'{paths.Root}' already contains a vault.");
        }

        if (File.Exists(paths.DatabaseFile))
        {
            throw new VaultException(
                $"'{paths.Root}' holds a database but no keyring. Its data cannot be decrypted; " +
                "restore vault.json from a backup rather than overwriting it.");
        }

        return new PendingVault(
            paths,
            VaultKeyring.Prepare(paths.KeyringFile, deviceKeyStore ?? DeviceKeyStore.ForCurrentPlatform()));
    }

    /// <summary>Writes the prepared vault to disk and opens it. The first moment anything exists.</summary>
    public static VaultCreation Commit(PendingVault pending)
    {
        var paths = pending.Paths;
        var keys = pending.Keys;

        paths.EnsureDirectories();

        try
        {
            keys.Keyring.Persist();
            InitialiseDatabase(paths, keys.Keyring.VaultId, keys.DataKey);

            // The data key now belongs to the OpenVault; disposing the PendingVault must no longer
            // zero it. Handing it over is the whole point of committing.
            pending.HandOver();

            return new VaultCreation(new OpenVault(paths, keys.Keyring, keys.DataKey), keys.RecoveryCode);
        }
        catch
        {
            keys.DataKey.Dispose();
            throw;
        }
    }

    /// <summary>The everyday path: no prompt, no passphrase, just open.</summary>
    public static OpenVault UnlockWithDeviceKey(string directory, IDeviceKeyStore? deviceKeyStore = null)
    {
        var paths = Locate(directory);
        var keyring = VaultKeyring.Load(paths.KeyringFile);
        var dataKey = keyring.UnlockWithDeviceKey(deviceKeyStore ?? DeviceKeyStore.ForCurrentPlatform());
        return Verify(paths, keyring, dataKey);
    }

    /// <summary>The disaster path: new machine, dead laptop, or a device key that no longer applies.</summary>
    public static OpenVault UnlockWithRecoveryCode(string directory, string recoveryCode)
    {
        var paths = Locate(directory);
        var keyring = VaultKeyring.Load(paths.KeyringFile);
        var dataKey = keyring.UnlockWithRecoveryCode(recoveryCode);
        return Verify(paths, keyring, dataKey);
    }

    public static OpenVault UnlockWithPassphrase(string directory, string passphrase)
    {
        var paths = Locate(directory);
        var keyring = VaultKeyring.Load(paths.KeyringFile);
        var dataKey = keyring.UnlockWithPassphrase(passphrase);
        return Verify(paths, keyring, dataKey);
    }

    /// <summary>Reads the keyring without unlocking, so the UI can show which unlock paths exist.</summary>
    public static VaultKeyring InspectKeyring(string directory) =>
        VaultKeyring.Load(Locate(directory).KeyringFile);

    private static VaultPaths Locate(string directory)
    {
        var paths = new VaultPaths(directory);
        if (!paths.Exists)
        {
            throw new VaultCorruptedException($"No vault found in '{paths.Root}'.");
        }

        return paths;
    }

    private static OpenVault Verify(VaultPaths paths, VaultKeyring keyring, SecretKey dataKey)
    {
        try
        {
            using (var connection = VaultDatabase.Open(paths.DatabaseFile, dataKey))
            {
                var storedId = ReadMetadata(connection, "vault_id");

                // A right-key/wrong-database mix-up decrypts cleanly and then quietly writes one
                // practice's records into another's file. Cheap to check, miserable to debug.
                if (storedId is not null && !Guid.Parse(storedId).Equals(keyring.VaultId))
                {
                    throw new VaultCorruptedException(
                        $"'{paths.DatabaseFile}' belongs to vault {storedId}, but this keyring is for {keyring.VaultId}.");
                }
            }

            return new OpenVault(paths, keyring, dataKey);
        }
        catch
        {
            dataKey.Dispose();
            throw;
        }
    }

    private static void InitialiseDatabase(VaultPaths paths, Guid vaultId, SecretKey dataKey)
    {
        using var connection = VaultDatabase.Open(paths.DatabaseFile, dataKey);
        using var command = connection.CreateCommand();

        // Also materialises the file: until something is written, a keyed-but-empty SQLite database is
        // zero bytes on disk, and LooksEncrypted would have nothing to assert against.
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS vault_metadata (
                key   TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            ) STRICT;

            INSERT OR REPLACE INTO vault_metadata (key, value) VALUES ('vault_id', $vaultId);
            INSERT OR REPLACE INTO vault_metadata (key, value) VALUES ('created_at', $createdAt);
            """;
        command.Parameters.AddWithValue("$vaultId", vaultId.ToString());
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string? ReadMetadata(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT value FROM vault_metadata WHERE key = $key
              AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'vault_metadata');
            """;
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }
}
