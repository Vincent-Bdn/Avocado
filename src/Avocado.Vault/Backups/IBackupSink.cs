namespace Avocado.Vault.Backups;

/// <summary>One object already on a sink.</summary>
/// <param name="Path">Sink-relative, forward slashes, as handed to <see cref="IBackupSink.WriteAsync"/>.</param>
public readonly record struct SinkEntry(string Path, long SizeBytes, DateTimeOffset ModifiedAt);

public enum SinkStatus
{
    /// <summary>Reachable and writable right now.</summary>
    Ready,

    /// <summary>Nothing wrong, it is simply not here: the key is unplugged, the laptop is off the network.</summary>
    Absent,

    /// <summary>It should be here and is not answering. A NAS that times out, an expired token.</summary>
    Unreachable,

    /// <summary>Found, refused. Read-only volume, revoked permission, full disk.</summary>
    Denied,
}

/// <param name="Detail">Shown to the user as-is, in French. Null when there is nothing useful to add.</param>
/// <param name="Location">Where it turned out to be, for a destination whose address moves. « E:\ », a Drive folder.</param>
public sealed record SinkProbe(SinkStatus Status, string? Detail = null, string? Location = null)
{
    public bool IsReady => Status is SinkStatus.Ready;

    public static SinkProbe Ready(string? location = null) => new(SinkStatus.Ready, Location: location);
}

/// <summary>
/// Somewhere a copy of the vault can live. A folder, a USB key, a NAS share, Google Drive, one day an
/// S3 bucket.
///
/// <para>Deliberately an object store and not a filesystem: create-directory, rename, seek and
/// permissions have no honest meaning on half the destinations we are heading for, and a sink that
/// promised them would have to lie. What every one of them can genuinely do is list under a prefix,
/// write a whole object, read one back and delete one. That is the whole interface.</para>
///
/// <para>Paths are sink-relative and use forward slashes on every platform, because on most of these
/// destinations a path is a key rather than a route through directories. <see cref="DirectorySink"/>
/// is the one that has to translate.</para>
///
/// <para>Implementations must be safe to call from the backup service's timer while the user is
/// clicking « Sauvegarder maintenant » in the window.</para>
/// </summary>
public interface IBackupSink
{
    /// <summary>What the user called it. « Clé USB Sauvegarde », « Google Drive ».</summary>
    string DisplayName { get; }

    /// <summary>
    /// Cheap enough to call every thirty seconds, because that is what finds the key someone just
    /// plugged in. Never throws: a destination that is not there is an answer, not a failure.
    /// </summary>
    Task<SinkProbe> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Every object whose path starts with <paramref name="prefix"/>. Empty if none.</summary>
    Task<IReadOnlyList<SinkEntry>> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="content"/> at <paramref name="path"/>, replacing whatever was there.
    /// Must be atomic as far as the destination allows: a reader must never see half an object, since
    /// the half it would see is somebody's only copy of their practice.
    /// </summary>
    Task WriteAsync(string path, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Deletes it. Absent is success: the caller wanted it gone and it is.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
