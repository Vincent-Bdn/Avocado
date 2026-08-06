namespace Avocado.Vault.Storage;

/// <summary>
/// A live SQLite database inside Dropbox, OneDrive or Google Drive will eventually be corrupted: the
/// sync client uploads the file mid-write, or restores a stale copy over a newer WAL. The rule is
/// backups go to the cloud, the vault never does — so this check exists to refuse the folder up front
/// rather than explain the corruption six months later.
/// </summary>
public static class CloudSyncDetector
{
    private static readonly string[] DirectoryMarkers =
    [
        "onedrive",
        "dropbox",
        "google drive",
        "googledrive",
        "my drive",
        "icloud drive",
        "icloud~",
        "nextcloud",
        "pcloud",
        "mega",
        "syncthing",
        "creative cloud files",
    ];

    private static readonly string[] FileMarkers =
    [
        ".dropbox",
        ".dropbox.cache",
        ".sync.ffs_db",
        ".csync_journal.db",
    ];

    /// <summary>
    /// Best effort. It walks up from the folder looking for a sync root by name or by the marker files
    /// those clients leave behind. False negatives are possible — a user can point OneDrive at any
    /// folder — so this reduces the failure rate, it does not eliminate it.
    /// </summary>
    public static bool IsInsideSyncedFolder(string directory, out string? detectedRoot)
    {
        detectedRoot = null;

        var current = new DirectoryInfo(Path.GetFullPath(directory));
        while (current is not null)
        {
            var name = current.Name.ToLowerInvariant();
            if (DirectoryMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal)))
            {
                detectedRoot = current.FullName;
                return true;
            }

            if (current.Exists && FileMarkers.Any(marker =>
                    File.Exists(Path.Combine(current.FullName, marker)) ||
                    Directory.Exists(Path.Combine(current.FullName, marker))))
            {
                detectedRoot = current.FullName;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <exception cref="VaultException">The folder is inside a known cloud-sync root.</exception>
    public static void ThrowIfInsideSyncedFolder(string directory)
    {
        if (IsInsideSyncedFolder(directory, out var root))
        {
            throw new SyncedFolderException(
                $"This folder is inside '{root}', which looks like a cloud-synced folder. " +
                "A live database there will be corrupted by the sync client. " +
                "Put the vault on a local disk and point automatic backups at the synced folder instead.",
                root!);
        }
    }
}
