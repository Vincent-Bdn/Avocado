using Avocado.Vault.Crypto;
using Avocado.Vault.Keys;
using Avocado.Vault.Storage;
using Microsoft.Data.Sqlite;

namespace Avocado.Vault;

/// <param name="RecoveryCode">
/// Displayed once, then unobtainable. The setup wizard must not let the user past this without
/// printing it or writing it to a USB key — it is what makes their backups restorable.
/// </param>
public sealed record VaultCreation(OpenVault Vault, string RecoveryCode);

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

        paths.EnsureDirectories();

        var creation = VaultKeyring.Create(paths.KeyringFile, deviceKeyStore ?? DeviceKeyStore.ForCurrentPlatform());
        try
        {
            InitialiseDatabase(paths, creation.Keyring.VaultId, creation.DataKey);
            return new VaultCreation(new OpenVault(paths, creation.Keyring, creation.DataKey), creation.RecoveryCode);
        }
        catch
        {
            creation.DataKey.Dispose();
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
