namespace Avocado.Server.Features.Backups;

/// <summary>
/// Somewhere the cabinet's data is copied to, as configured by the user.
///
/// <para>One row per destination rather than a settings key, because there is genuinely more than one
/// and they each carry state: when they last worked, what went wrong, how much history to keep. The
/// row is configuration; turning it into something that can read and write is
/// <see cref="Infrastructure.SinkFactory"/>'s job.</para>
///
/// <para>This table lives in the vault database, which is SQLCipher-encrypted, and that is what makes
/// it a safe place for <see cref="Secret"/>. A Google refresh token is a key to the backup copy of
/// everything; keeping it in a config file beside the vault would hand over the backups to anyone who
/// reads the disk, having gone to the trouble of encrypting the original.</para>
/// </summary>
public class BackupDestination
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Which kind of sink to build. Open text rather than an enum, since the set grows, and a value
    /// this version does not recognise is reported and skipped rather than crashing a downgrade.
    /// </summary>
    public string Kind { get; set; } = BackupDestinationKinds.Folder;

    /// <summary>Hers. « Clé USB du cabinet », « Google Drive », « NAS du bureau ».</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>For <see cref="BackupDestinationKinds.Folder"/>: the folder. Null otherwise.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// For <see cref="BackupDestinationKinds.Volume"/>: the id written into the marker file on the
    /// device, which is what identifies it wherever it mounts. See Avocado.Vault's SinkMarker.
    /// </summary>
    public Guid? VolumeId { get; set; }

    /// <summary>
    /// Whatever the sink needs to authenticate, as JSON. A refresh token for Drive; nothing at all for
    /// a folder. Encrypted at rest by the database it sits in.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>The remote's own idea of where to put things. A Drive folder id, one day a bucket.</summary>
    public string? RemoteFolderId { get; set; }

    /// <summary>Off keeps the row and stops the copying, which is what someone wants when a key is lost.</summary>
    public bool IsEnabled { get; set; } = true;

    public int KeepNewest { get; set; } = 12;

    public int KeepDailyForDays { get; set; } = 60;

    /// <summary>
    /// The last time a copy actually landed here. The single most important field in the table: it is
    /// what the window turns into « si votre ordinateur disparaissait maintenant… ».
    /// </summary>
    public DateTimeOffset? LastBackupAt { get; set; }

    /// <summary>Last time it was reachable at all, whether or not there was anything to send.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>
    /// In French, ready to show. Cleared on the next success. Not set merely because a USB key is
    /// unplugged: that is the normal state of a USB key and reporting it as an error teaches people to
    /// ignore the warning that matters.
    /// </summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class BackupDestinationKinds
{
    /// <summary>
    /// A path. Which is most things: a second disk, a NAS share the OS has mounted, and the local
    /// Google Drive, OneDrive or Dropbox folder, where the desktop client uploads it and Avocado is
    /// none the wiser.
    /// </summary>
    public const string Folder = "folder";

    /// <summary>A removable device, found by its marker rather than by a drive letter that moves.</summary>
    public const string Volume = "volume";

    /// <summary>Google Drive over its own API, for the many people who have never installed the desktop client.</summary>
    public const string GoogleDrive = "googleDrive";
}
