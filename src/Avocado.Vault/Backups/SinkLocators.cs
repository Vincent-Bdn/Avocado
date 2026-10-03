namespace Avocado.Vault.Backups;

/// <summary>
/// Answers "where is this destination right now", which is a different question from "how do I read
/// and write it".
///
/// <para>Splitting the two is what stops removable media from needing a sink of its own. A USB key, a
/// NAS share and a synced folder are all a folder once you have found them; only the finding differs.
/// One <see cref="DirectorySink"/> serves all three, and adding "my second hard disk" later is a
/// locator, not an implementation.</para>
/// </summary>
public interface ISinkLocator
{
    string DisplayName { get; }

    /// <summary>The folder to use, or null when the destination is genuinely not here.</summary>
    Task<string?> LocateAsync(CancellationToken cancellationToken = default);
}

/// <summary>A destination that does not move: a folder on the internal disk, a mounted share, the
/// Google Drive or OneDrive folder the desktop client keeps in sync.</summary>
public sealed class FixedPathLocator(string path, string displayName) : ISinkLocator
{
    public string DisplayName => displayName;

    public Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        // The folder itself, not merely its parent.
        //
        // It used to accept a missing folder whose parent existed, so that the first backup could
        // create it. On a network share that is how backups end up on the wrong disk: an unmounted
        // NAS leaves /Volumes, /mnt or the drive letter's parent perfectly present, the folder gets
        // created there on the local disk, and every copy after that is written to a path that only
        // looks like the NAS. The destination reports success, « dernière copie » keeps moving, and
        // nothing is where she thinks it is.
        //
        // Nothing is lost by requiring it: she picked an existing folder from a picker. A folder that
        // has since gone is a destination that is genuinely absent, which is what the screen should
        // say and is exactly how an unplugged USB key already behaves.
        var full = Path.GetFullPath(path);

        return Task.FromResult(Directory.Exists(full) ? full : null);
    }
}

/// <summary>
/// A destination identified by the marker Avocado left on it, wherever the operating system has
/// decided to mount it today. See <see cref="SinkMarker"/> for why the drive letter is not the
/// identity.
/// </summary>
public sealed class MarkedVolumeLocator(Guid sinkId, string displayName) : ISinkLocator
{
    public string DisplayName => displayName;

    public Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        foreach (var root in VolumeScanner.CandidateRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SinkMarker.Read(root)?.SinkId == sinkId)
            {
                return Task.FromResult<string?>(root);
            }
        }

        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// Another locator's destination, narrowed to a folder inside it.
///
/// <para>Wrapping rather than teaching each locator about subfolders: a volume is still found by its
/// marker, wherever the operating system mounted it today, and this only decides where to work once
/// it has been found. The folder is created on demand, because unlike a mount point it is genuinely
/// ours to make: we are already standing on the device.</para>
/// </summary>
public sealed class SubfolderLocator(ISinkLocator inner, string subpath) : ISinkLocator
{
    public string DisplayName => inner.DisplayName;

    public async Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        if (await inner.LocateAsync(cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return null;
        }

        var cleaned = Clean(subpath);
        if (cleaned.Length == 0)
        {
            return root;
        }

        var full = Path.GetFullPath(Path.Combine(root, cleaned));

        // Belt and braces against « ../.. » in a stored row: the whole point is to stay on the
        // device that was found, and a subpath that climbs out of it is not a subpath.
        return full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal) ? full : null;
    }

    /// <summary>« /Sauvegardes/Avocado/ » and « Sauvegardes\Avocado » are the same place.</summary>
    public static string Clean(string? subpath) =>
        string.Join(
            Path.DirectorySeparatorChar,
            (subpath ?? string.Empty)
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => part != "." && part != ".."));
}

/// <summary>
/// Every place a removable volume might have turned up. Deliberately generous and deliberately cheap:
/// this runs every thirty seconds so that plugging a key in is all the user has to do.
/// </summary>
public static class VolumeScanner
{
    public static IEnumerable<string> CandidateRoots()
    {
        foreach (var drive in SafeDrives())
        {
            yield return drive;
        }

        // Where the three desktop platforms actually mount removable media. DriveInfo covers Windows
        // letters and the Unix mount table, but a key plugged into a Mac shows up as a directory
        // under /Volumes that the mount table does not always volunteer.
        foreach (var parent in new[]
                 {
                     "/Volumes",
                     "/media",
                     "/run/media",
                     "/mnt",
                     Path.Combine("/media", Environment.UserName),
                     Path.Combine("/run/media", Environment.UserName),
                 })
        {
            if (!Directory.Exists(parent))
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(parent);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return [];
        }

        return drives
            .Where(drive =>
            {
                // IsReady throws on a card reader with no card in it, and on a network drive whose
                // server has gone away. Both mean "not now", not "crash the backup service".
                try
                {
                    return drive.IsReady;
                }
                catch (IOException)
                {
                    return false;
                }
            })
            .Select(drive => drive.RootDirectory.FullName);
    }
}
