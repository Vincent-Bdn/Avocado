using Avocado.Vault.Storage;

namespace Avocado.Vault.Tests;

public class VaultManagerTests
{
    [Fact]
    public void CreateLaysOutTheVaultFolder()
    {
        using var directory = new TempDirectory();

        var creation = VaultManager.Create(directory.Path, new FakeDeviceKeyStore());
        using var vault = creation.Vault;

        Assert.True(File.Exists(vault.Paths.KeyringFile));
        Assert.True(File.Exists(vault.Paths.DatabaseFile));
        Assert.True(Directory.Exists(vault.Paths.BlobsDirectory));
        Assert.True(Directory.Exists(vault.Paths.BackupsDirectory));
        Assert.True(VaultDatabase.LooksEncrypted(vault.Paths.DatabaseFile));
        Assert.NotEmpty(creation.RecoveryCode);
    }

    [Fact]
    public void ReopensWithTheDeviceKey()
    {
        using var directory = new TempDirectory();
        var deviceKeyStore = new FakeDeviceKeyStore();

        var id = Create(directory, deviceKeyStore, out _);

        using var reopened = VaultManager.UnlockWithDeviceKey(directory.Path, deviceKeyStore);
        Assert.Equal(id, reopened.Id);
    }

    [Fact]
    public void RecoversOntoAReplacementMachine()
    {
        // The whole point of the recovery key: the laptop is gone, so its DPAPI blob is meaningless.
        using var directory = new TempDirectory();
        var id = Create(directory, new FakeDeviceKeyStore("Drowned laptop"), out var recoveryCode);

        using var recovered = VaultManager.UnlockWithRecoveryCode(directory.Path, recoveryCode);

        Assert.Equal(id, recovered.Id);

        // And the new machine can then enrol itself so the daily path works again.
        var replacement = new FakeDeviceKeyStore("Replacement laptop");
        recovered.EnrollDeviceKey(replacement);

        using var everyday = VaultManager.UnlockWithDeviceKey(directory.Path, replacement);
        Assert.Equal(id, everyday.Id);
    }

    [Fact]
    public void RefusesAWrongRecoveryCode()
    {
        using var directory = new TempDirectory();
        Create(directory, new FakeDeviceKeyStore(), out _);

        using var other = new TempDirectory();
        Create(other, new FakeDeviceKeyStore(), out var unrelatedCode);

        Assert.Throws<VaultUnlockFailedException>(
            () => VaultManager.UnlockWithRecoveryCode(directory.Path, unrelatedCode));
    }

    [Fact]
    public void VerifyRecoveryCodeConfirmsThePrintedSheetStillWorks()
    {
        using var directory = new TempDirectory();
        var creation = VaultManager.Create(directory.Path, new FakeDeviceKeyStore());
        using var vault = creation.Vault;

        Assert.True(vault.VerifyRecoveryCode(creation.RecoveryCode));
        Assert.False(vault.VerifyRecoveryCode("XXXXXX-XXXXXX-XXXXXX"));

        // After regenerating, the old sheet must stop verifying, otherwise the quarterly check would
        // reassure the user about a code that no longer opens anything.
        var replacement = vault.RegenerateRecoveryKey();
        Assert.True(vault.VerifyRecoveryCode(replacement));
        Assert.False(vault.VerifyRecoveryCode(creation.RecoveryCode));
    }

    [Fact]
    public void RefusesToCreateAVaultInsideASyncedFolder()
    {
        using var directory = new TempDirectory();
        var syncedPath = Path.Combine(directory.Path, "Dropbox", "Cabinet");
        Directory.CreateDirectory(syncedPath);

        var exception = Assert.Throws<SyncedFolderException>(
            () => VaultManager.Create(syncedPath, new FakeDeviceKeyStore()));

        Assert.Contains("Dropbox", exception.DetectedRoot, StringComparison.OrdinalIgnoreCase);

        // Still possible when the user insists, since the detector is a heuristic.
        using var forced = VaultManager.Create(syncedPath, new FakeDeviceKeyStore(), allowSyncedFolder: true).Vault;
        Assert.True(File.Exists(forced.Paths.KeyringFile));
    }

    [Fact]
    public void RefusesToCreateOverAnExistingVault()
    {
        using var directory = new TempDirectory();
        Create(directory, new FakeDeviceKeyStore(), out _);

        Assert.Throws<VaultException>(() => VaultManager.Create(directory.Path, new FakeDeviceKeyStore()));
    }

    [Fact]
    public void RefusesToCreateOverAnOrphanedDatabase()
    {
        // A database whose keyring was lost is undecryptable, and overwriting it would destroy the
        // only copy of the practice. Fail loudly and point at the backups instead.
        using var directory = new TempDirectory();
        Create(directory, new FakeDeviceKeyStore(), out _);
        File.Delete(Path.Combine(directory.Path, "vault.json"));

        var exception = Assert.Throws<VaultException>(() => VaultManager.Create(directory.Path, new FakeDeviceKeyStore()));
        Assert.Contains("restore vault.json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesADatabaseBelongingToAnotherVault()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();
        var deviceKeyStore = new FakeDeviceKeyStore();

        Create(first, deviceKeyStore, out _);
        var creation = VaultManager.Create(second.Path, deviceKeyStore);
        var code = creation.RecoveryCode;
        creation.Vault.Dispose();

        File.Copy(
            Path.Combine(first.Path, "avocado.db"),
            Path.Combine(second.Path, "avocado.db"),
            overwrite: true);

        // Each vault has its own data encryption key, so a swapped database is caught at the cipher
        // layer before the id check ever runs.
        Assert.Throws<VaultUnlockFailedException>(
            () => VaultManager.UnlockWithRecoveryCode(second.Path, code));
    }

    [Fact]
    public void RefusesADatabaseWhoseRecordedVaultIdDoesNotMatch()
    {
        // Defence in depth behind the key check above: it is what would catch a future multi-user or
        // shared-key arrangement pointing a keyring at the wrong practice's file, where decryption
        // itself would succeed.
        using var directory = new TempDirectory();
        var deviceKeyStore = new FakeDeviceKeyStore();
        Create(directory, deviceKeyStore, out _);

        using (var vault = VaultManager.UnlockWithDeviceKey(directory.Path, deviceKeyStore))
        using (var connection = vault.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE vault_metadata SET value = $id WHERE key = 'vault_id';";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<VaultCorruptedException>(
            () => VaultManager.UnlockWithDeviceKey(directory.Path, deviceKeyStore));
        Assert.Contains("belongs to vault", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnAbsentVault()
    {
        using var directory = new TempDirectory();

        Assert.Throws<VaultCorruptedException>(
            () => VaultManager.UnlockWithDeviceKey(directory.Path, new FakeDeviceKeyStore()));
    }

    [Fact]
    public void CreatesABackupOfTheDatabase()
    {
        using var directory = new TempDirectory();
        var creation = VaultManager.Create(directory.Path, new FakeDeviceKeyStore());
        using var vault = creation.Vault;

        var backupPath = vault.CreateBackup("before-migration");

        Assert.True(File.Exists(backupPath));
        Assert.StartsWith(vault.Paths.BackupsDirectory, backupPath, StringComparison.Ordinal);
        Assert.Contains("before-migration", Path.GetFileName(backupPath), StringComparison.Ordinal);
        Assert.True(VaultDatabase.LooksEncrypted(backupPath));
    }

    [Fact]
    public void InspectKeyringShowsUnlockPathsWithoutUnlocking()
    {
        using var directory = new TempDirectory();
        Create(directory, new FakeDeviceKeyStore("Anne's laptop"), out _);

        var keyring = VaultManager.InspectKeyring(directory.Path);

        Assert.True(keyring.HasDeviceKey);
        Assert.True(keyring.HasRecoveryKey);
        Assert.False(keyring.HasPassphrase);
        Assert.Contains(keyring.Keys, k => k.Label == "Anne's laptop");
    }

    [Fact]
    public void DisposingRelocksTheVault()
    {
        using var directory = new TempDirectory();
        var vault = VaultManager.Create(directory.Path, new FakeDeviceKeyStore()).Vault;

        vault.Dispose();

        Assert.Throws<ObjectDisposedException>(() => vault.OpenConnection());
    }

    [Fact]
    public void StoresDocumentsAndRecordsTogether()
    {
        using var directory = new TempDirectory();
        var deviceKeyStore = new FakeDeviceKeyStore();

        BlobReferenceRoundtrip(directory, deviceKeyStore);

        using var reopened = VaultManager.UnlockWithDeviceKey(directory.Path, deviceKeyStore);
        using var connection = reopened.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sha256 FROM documents;";
        var sha256 = (string)command.ExecuteScalar()!;

        var content = ReadBlob(reopened, sha256);
        Assert.Equal("Assignation devant le tribunal judiciaire", content);
    }

    public sealed class SingleVaultStoreTests
    {
        [Fact]
        public void ResolvesItsOwnVaultAndRejectsOthers()
        {
            using var directory = new TempDirectory();
            using var store = new SingleVaultStore(VaultManager.Create(directory.Path, new FakeDeviceKeyStore()).Vault);

            Assert.Same(store.Vault, store.Get(store.Vault.Id));
            Assert.Same(store.Vault, store.Get(Guid.Empty));
            Assert.Throws<VaultException>(() => store.Get(Guid.NewGuid()));

            Assert.True(store.TryGet(store.Vault.Id, out var found));
            Assert.Same(store.Vault, found);
            Assert.False(store.TryGet(Guid.NewGuid(), out var missing));
            Assert.Null(missing);
        }
    }

    private static Guid Create(TempDirectory directory, FakeDeviceKeyStore deviceKeyStore, out string recoveryCode)
    {
        var creation = VaultManager.Create(directory.Path, deviceKeyStore);
        recoveryCode = creation.RecoveryCode;
        var id = creation.Vault.Id;
        creation.Vault.Dispose();
        return id;
    }

    private static void BlobReferenceRoundtrip(TempDirectory directory, FakeDeviceKeyStore deviceKeyStore)
    {
        using var vault = VaultManager.Create(directory.Path, deviceKeyStore).Vault;

        var reference = vault.Blobs
            .PutAsync(new MemoryStream("Assignation devant le tribunal judiciaire"u8.ToArray()))
            .GetAwaiter().GetResult();

        using var connection = vault.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE documents (sha256 TEXT NOT NULL, size_bytes INTEGER NOT NULL) STRICT;
            INSERT INTO documents VALUES ($sha256, $size);
            """;
        command.Parameters.AddWithValue("$sha256", reference.Sha256);
        command.Parameters.AddWithValue("$size", reference.SizeBytes);
        command.ExecuteNonQuery();
    }

    private static string ReadBlob(OpenVault vault, string sha256)
    {
        using var stream = vault.Blobs.OpenRead(new Blobs.BlobReference(sha256, 0));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
