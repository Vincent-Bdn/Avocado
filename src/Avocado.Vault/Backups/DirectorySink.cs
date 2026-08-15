namespace Avocado.Vault.Backups;

/// <summary>
/// A sink that is a folder. Which is nearly all of them: a second disk, a USB key, a NAS share the OS
/// has already mounted, and the local Google Drive, OneDrive or Dropbox folder, where the desktop
/// client does the uploading and we are none the wiser. That last case is the reason this class covers
/// so much ground for so little code, and the reason a synced folder needs no feature of its own.
///
/// <para>The root is resolved through an <see cref="ISinkLocator"/> rather than stored, because the
/// interesting destinations move: a USB key is E:\ today and F:\ tomorrow.</para>
/// </summary>
public sealed class DirectorySink(ISinkLocator locator) : IBackupSink
{
    public string DisplayName => locator.DisplayName;

    public async Task<SinkProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var located = await locator.LocateAsync(cancellationToken).ConfigureAwait(false);
        if (located is not { } root)
        {
            return new SinkProbe(SinkStatus.Absent);
        }

        // Writable, not merely present. A key with the tab flicked to read-only, a share mounted
        // without write permission and a full disk all look identical until something is written, and
        // finding out at that point means the failure surfaces halfway through a backup.
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".avocado-write-test-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [], cancellationToken).ConfigureAwait(false);
            File.Delete(probe);

            return SinkProbe.Ready(root);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new SinkProbe(SinkStatus.Denied, exception.Message, root);
        }
    }

    public async Task<IReadOnlyList<SinkEntry>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        var root = await RequireRootAsync(cancellationToken).ConfigureAwait(false);
        var start = Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(start))
        {
            return [];
        }

        var entries = new List<SinkEntry>();
        foreach (var file in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            entries.Add(new SinkEntry(
                Path.GetRelativePath(root, file).Replace('\\', '/'),
                info.Length,
                info.LastWriteTimeUtc));
        }

        return entries;
    }

    public async Task WriteAsync(
        string path,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var root = await RequireRootAsync(cancellationToken).ConfigureAwait(false);
        var destination = Resolve(root, path);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Written beside the target and moved into place. A key pulled out mid-write then leaves a
        // stray .part rather than a truncated blob that looks like the real thing and decrypts to
        // nothing.
        var temporary = destination + ".part";

        try
        {
            var file = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);

            await using (file.ConfigureAwait(false))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = await RequireRootAsync(cancellationToken).ConfigureAwait(false);
        return File.OpenRead(Resolve(root, path));
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = await RequireRootAsync(cancellationToken).ConfigureAwait(false);
        TryDelete(Resolve(root, path));
    }

    private static string Resolve(string root, string path)
    {
        var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

        // A path is a key here, and keys come from a manifest that lived on the destination. Refusing
        // to climb out of the root is what keeps a tampered manifest from writing into the rest of
        // the disk.
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException($"'{path}' escapes the backup destination.");
        }

        return full;
    }

    private async Task<string> RequireRootAsync(CancellationToken cancellationToken) =>
        await locator.LocateAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new SinkUnavailableException(
            $"La destination de sauvegarde « {locator.DisplayName} » n'est pas connectée.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Nothing useful to do. The next pass tries again.
        }
    }
}
