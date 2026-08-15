using System.Globalization;

namespace Avocado.Vault.Backups;

/// <summary>
/// Where things sit inside a sink.
///
/// <para>The layout is a mirror of the vault folder, not an archive format: <c>vault.json</c>, the
/// blobs under their own names, and the database as a series of dated snapshots. Someone who opens
/// the USB key sees something recognisable, someone restoring by hand can copy three things into a
/// folder, and someone reading this in five years does not have to reverse-engineer a container.</para>
///
/// <para>Every path is namespaced by vault id, so one destination can hold several vaults, which is
/// what happens the day an associate's key gets used for both cabinets.</para>
/// </summary>
public static class BackupLayout
{
    /// <summary>Dropped at the root of a destination so it can be recognised wherever it mounts.</summary>
    public const string MarkerFile = ".avocado-sink.json";

    public const string SnapshotExtension = ".db";

    public static string VaultPrefix(Guid vaultId) => $"avocado/{vaultId:D}";

    public static string Keyring(Guid vaultId) => $"{VaultPrefix(vaultId)}/vault.json";

    /// <summary>Human-readable, and the one file that says what this pile of blobs is.</summary>
    public static string Manifest(Guid vaultId) => $"{VaultPrefix(vaultId)}/manifest.json";

    public static string BlobPrefix(Guid vaultId) => $"{VaultPrefix(vaultId)}/blobs";

    public static string SnapshotPrefix(Guid vaultId) => $"{VaultPrefix(vaultId)}/snapshots";

    /// <summary>
    /// The sink path for a blob, given its path relative to the vault's own blob folder. The sharded
    /// <c>ab/cd/…</c> shape is kept: it is what stops a directory from holding a hundred thousand
    /// entries, and it costs nothing on the destinations where it means nothing.
    /// </summary>
    public static string Blob(Guid vaultId, string relativePath) =>
        $"{BlobPrefix(vaultId)}/{relativePath.Replace('\\', '/')}";

    /// <summary>
    /// A snapshot keeps the name it was given locally, so pushing one is a copy rather than a
    /// translation, and the file on the USB key and the file in the vault are recognisably the same
    /// thing.
    /// </summary>
    public static string Snapshot(Guid vaultId, string fileName) =>
        $"{SnapshotPrefix(vaultId)}/{fileName}";

    /// <summary>
    /// The instant back out of a snapshot name, so a restore screen can say what it is offering.
    /// Names are <c>yyyyMMdd-HHmmss-label.db</c>, UTC and sortable, which is what makes ordering by
    /// name and ordering by time the same thing.
    /// </summary>
    public static DateTimeOffset? SnapshotTakenAt(string path)
    {
        const int stampLength = 15; // yyyyMMdd-HHmmss

        var name = path.Split('/')[^1];
        if (!name.EndsWith(SnapshotExtension, StringComparison.Ordinal) || name.Length < stampLength)
        {
            return null;
        }

        return DateTime.TryParseExact(
            name[..stampLength],
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }
}
