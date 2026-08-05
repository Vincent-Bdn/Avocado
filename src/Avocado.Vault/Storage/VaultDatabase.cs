using System.Text;
using Avocado.Vault.Crypto;
using Microsoft.Data.Sqlite;

namespace Avocado.Vault.Storage;

/// <summary>
/// Opens the SQLCipher database. Everything here exists because of one of the three footguns in the
/// spec: the wrong native bundle silently writing plaintext, the raw key having to be passed as a hex
/// literal rather than through the connection string, and pooled connections coming back unkeyed.
/// </summary>
public static class VaultDatabase
{
    /// <summary>Header written by unencrypted SQLite. If we ever see it, encryption is not on.</summary>
    private static readonly byte[] PlaintextSqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    static VaultDatabase() => SQLitePCL.Batteries_V2.Init();

    /// <summary>
    /// Opens and keys a connection. The caller owns it and must dispose it.
    /// <para>
    /// Pooling is disabled deliberately. Microsoft.Data.Sqlite pools by connection string, and a
    /// pooled handle comes back already keyed — re-issuing <c>PRAGMA key</c> on it misbehaves, and in
    /// the multi-tenant case a handle keyed for one vault must never be reachable from another.
    /// </para>
    /// </summary>
    /// <exception cref="VaultUnlockFailedException">The key does not open this database.</exception>
    public static SqliteConnection Open(string databasePath, SecretKey dataKey, bool readOnly = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            ApplyKey(connection, dataKey);
            AssertSqlCipherIsActive(connection);
            AssertKeyIsCorrect(connection);

            if (!readOnly)
            {
                Execute(connection, "PRAGMA journal_mode = WAL;");
                Execute(connection, "PRAGMA synchronous = NORMAL;");
            }

            Execute(connection, "PRAGMA foreign_keys = ON;");
            Execute(connection, "PRAGMA busy_timeout = 5000;");

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes a consistent snapshot to <paramref name="destinationPath"/>, encrypted with the same key.
    /// <para>
    /// <c>VACUUM INTO</c> rather than a file copy: copying <c>avocado.db</c> while a WAL is live
    /// produces a snapshot missing its most recent commits, which is precisely the backup you would
    /// discover was useless at the worst moment.
    /// </para>
    /// </summary>
    public static void BackupTo(SqliteConnection connection, string destinationPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationPath))
        {
            throw new VaultException($"A backup already exists at '{destinationPath}'.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $destination;";
        command.Parameters.AddWithValue("$destination", Path.GetFullPath(destinationPath));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Cheap check that a file on disk is genuinely encrypted, by looking for the plaintext SQLite
    /// header. Used by the tests and by the startup self-check — the failure mode being guarded
    /// against is silent, so it has to be asserted rather than assumed.
    /// </summary>
    public static bool LooksEncrypted(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        using var stream = File.OpenRead(databasePath);
        Span<byte> header = stackalloc byte[PlaintextSqliteHeader.Length];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        // An empty file is not yet proof of anything either way.
        return read < header.Length || !header.SequenceEqual(PlaintextSqliteHeader);
    }

    private static void ApplyKey(SqliteConnection connection, SecretKey dataKey)
    {
        // Must be the very first statement on the connection.
        //
        // The connection string's `Password=` keyword is deliberately not used: it issues
        // `PRAGMA key = '<string>'`, which runs the value through SQLCipher's own KDF. Our key is
        // already a full-entropy 256-bit DEK, so it is passed as a raw hex literal instead.
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(dataKey.Span)}'\";";
        command.ExecuteNonQuery();
    }

    private static void AssertSqlCipherIsActive(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher_version;";
        var version = command.ExecuteScalar() as string;

        if (string.IsNullOrEmpty(version))
        {
            throw new VaultException(
                "This build is linked against plain SQLite, not SQLCipher, so vault data would be written " +
                "unencrypted. Check that SQLitePCLRaw.bundle_e_sqlcipher is referenced instead of " +
                "bundle_e_sqlite3.");
        }
    }

    private static void AssertKeyIsCorrect(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_schema;";
            command.ExecuteScalar();
        }
        catch (SqliteException ex)
        {
            throw new VaultUnlockFailedException("The key does not open this database.", ex);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
