using System.Text;
using Avocado.Vault.Crypto;
using Avocado.Vault.Storage;
using Microsoft.Data.Sqlite;

namespace Avocado.Vault.Tests;

public class VaultDatabaseTests
{
    [Fact]
    public void TheFileOnDiskIsActuallyEncrypted()
    {
        // The headline test. Referencing the wrong SQLitePCLRaw bundle writes plaintext with no error
        // and no exception — the only way to know is to look at the bytes.
        using var directory = new TempDirectory();
        var path = directory.Combine("avocado.db");
        using var key = SecretKey.Generate();

        using (var connection = VaultDatabase.Open(path, key))
        {
            Execute(connection, "CREATE TABLE t (v TEXT) STRICT;");
            Execute(connection, "INSERT INTO t (v) VALUES ('Dupont contre Martin');");
        }

        var bytes = File.ReadAllBytes(path);

        Assert.True(VaultDatabase.LooksEncrypted(path));
        Assert.False(StartsWith(bytes, "SQLite format 3\0"));
        Assert.DoesNotContain("Dupont contre Martin", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void UsesSqlCipherRatherThanPlainSqlite()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();

        using var connection = VaultDatabase.Open(directory.Combine("avocado.db"), key);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher_version;";

        Assert.False(string.IsNullOrEmpty(command.ExecuteScalar() as string));
    }

    [Fact]
    public void DataSurvivesCloseAndReopen()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("avocado.db");
        using var key = SecretKey.Generate();

        using (var connection = VaultDatabase.Open(path, key))
        {
            Execute(connection, "CREATE TABLE matters (reference TEXT) STRICT;");
            Execute(connection, "INSERT INTO matters VALUES ('2026-0042');");
        }

        using (var connection = VaultDatabase.Open(path, key))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT reference FROM matters;";
            Assert.Equal("2026-0042", command.ExecuteScalar());
        }
    }

    [Fact]
    public void RejectsTheWrongKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("avocado.db");
        using var key = SecretKey.Generate();
        using var wrongKey = SecretKey.Generate();

        using (var connection = VaultDatabase.Open(path, key))
        {
            Execute(connection, "CREATE TABLE t (v TEXT) STRICT;");
        }

        Assert.Throws<VaultUnlockFailedException>(() => VaultDatabase.Open(path, wrongKey));
    }

    [Fact]
    public void EnforcesForeignKeys()
    {
        using var directory = new TempDirectory();
        using var key = SecretKey.Generate();

        using var connection = VaultDatabase.Open(directory.Combine("avocado.db"), key);
        Execute(connection, "CREATE TABLE parent (id INTEGER PRIMARY KEY) STRICT;");
        Execute(connection, "CREATE TABLE child (parent_id INTEGER REFERENCES parent(id)) STRICT;");

        // Off by default in SQLite, which would let orphaned rows accumulate silently.
        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO child VALUES (999);"));
    }

    [Fact]
    public void BackupProducesAnEncryptedCopyReadableWithTheSameKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("avocado.db");
        var backupPath = directory.Combine("backups/snapshot.db");
        using var key = SecretKey.Generate();

        using (var connection = VaultDatabase.Open(path, key))
        {
            Execute(connection, "CREATE TABLE matters (reference TEXT) STRICT;");
            Execute(connection, "INSERT INTO matters VALUES ('2026-0042');");
            VaultDatabase.BackupTo(connection, backupPath);
        }

        Assert.True(VaultDatabase.LooksEncrypted(backupPath));

        using var restored = VaultDatabase.Open(backupPath, key);
        using var command = restored.CreateCommand();
        command.CommandText = "SELECT reference FROM matters;";
        Assert.Equal("2026-0042", command.ExecuteScalar());
    }

    [Fact]
    public void BackupCapturesCommitsStillSittingInTheWriteAheadLog()
    {
        // A plain file copy of avocado.db would miss these, producing a backup that silently loses the
        // most recent work.
        using var directory = new TempDirectory();
        var path = directory.Combine("avocado.db");
        var backupPath = directory.Combine("backups/snapshot.db");
        using var key = SecretKey.Generate();

        using var connection = VaultDatabase.Open(path, key);
        Execute(connection, "CREATE TABLE matters (reference TEXT) STRICT;");
        Execute(connection, "INSERT INTO matters VALUES ('2026-0042');");

        Assert.True(File.Exists(path + "-wal"), "expected WAL mode to be active");
        VaultDatabase.BackupTo(connection, backupPath);

        using var restored = VaultDatabase.Open(backupPath, key);
        using var command = restored.CreateCommand();
        command.CommandText = "SELECT count(*) FROM matters;";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void BackupRefusesToOverwrite()
    {
        using var directory = new TempDirectory();
        var backupPath = directory.Combine("backups/snapshot.db");
        using var key = SecretKey.Generate();

        using var connection = VaultDatabase.Open(directory.Combine("avocado.db"), key);
        Execute(connection, "CREATE TABLE t (v TEXT) STRICT;");
        VaultDatabase.BackupTo(connection, backupPath);

        Assert.Throws<VaultException>(() => VaultDatabase.BackupTo(connection, backupPath));
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool StartsWith(byte[] bytes, string prefix) =>
        bytes.Length >= prefix.Length && Encoding.ASCII.GetString(bytes, 0, prefix.Length) == prefix;
}

public class CloudSyncDetectorTests
{
    [Theory]
    [InlineData("OneDrive")]
    [InlineData("Dropbox")]
    [InlineData("Google Drive")]
    [InlineData("iCloud Drive")]
    public void DetectsAVaultPlacedInsideASyncRoot(string syncFolder)
    {
        using var directory = new TempDirectory();
        var vaultPath = Path.Combine(directory.Path, syncFolder, "Cabinet");
        Directory.CreateDirectory(vaultPath);

        Assert.True(CloudSyncDetector.IsInsideSyncedFolder(vaultPath, out var detected));
        Assert.Contains(syncFolder, detected, StringComparison.OrdinalIgnoreCase);

        // Its own type, so the UI can offer the override without matching on the message text.
        var exception = Assert.Throws<SyncedFolderException>(
            () => CloudSyncDetector.ThrowIfInsideSyncedFolder(vaultPath));

        Assert.Contains(syncFolder, exception.DetectedRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectsAClientMarkerFile()
    {
        using var directory = new TempDirectory();
        var syncRoot = Path.Combine(directory.Path, "Documents");
        var vaultPath = Path.Combine(syncRoot, "Cabinet");
        Directory.CreateDirectory(vaultPath);
        File.WriteAllText(Path.Combine(syncRoot, ".dropbox"), "");

        Assert.True(CloudSyncDetector.IsInsideSyncedFolder(vaultPath, out _));
    }

    [Fact]
    public void LeavesOrdinaryFoldersAlone()
    {
        using var directory = new TempDirectory();
        var vaultPath = Path.Combine(directory.Path, "Cabinet");
        Directory.CreateDirectory(vaultPath);

        Assert.False(CloudSyncDetector.IsInsideSyncedFolder(vaultPath, out _));
        CloudSyncDetector.ThrowIfInsideSyncedFolder(vaultPath);
    }
}
