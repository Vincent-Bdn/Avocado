using System.Collections.Concurrent;
using System.Security.Cryptography;
using Avocado.Server.Data;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Workspace;

/// <summary>
/// Opening a document in Word and getting the changes back into the coffre, without ever asking the
/// user to export, edit and re-upload.
///
/// <para><b>How it works.</b> A checkout decrypts the file into <c>&lt;coffre&gt;/.travail/&lt;id&gt;/</c>,
/// the shell hands that path to the operating system, and this service watches it. Every time the
/// bytes change on disk the file is re-encrypted back into the coffre as a new version of the same
/// document. A check-in re-reads it one last time and deletes the working copy.</para>
///
/// <para><b>Why polling rather than a FileSystemWatcher.</b> Word does not write documents in place.
/// It creates <c>~$name.docx</c> lock files and a scratch file, then renames over the original, so a
/// watcher sees a delete-and-create dance and has to be taught to read through it — and on Windows it
/// silently drops events when its buffer overflows. A 1.5-second comparison of (length, last write,
/// hash) has none of those failure modes and costs nothing for the handful of files that are ever
/// open at once. Correctness here is worth far more than latency.</para>
///
/// <para><b>What is on disk in clear.</b> Exactly the files currently open, inside the coffre folder,
/// for as long as they are open. That is the honest cost of letting Word edit them at all: no
/// application can hand a file to Word without the file existing. The working folder is emptied on
/// check-in and on a clean shutdown, and anything found there at startup is reported rather than
/// deleted — a crash must never silently discard an afternoon's drafting.</para>
/// </summary>
public sealed class DocumentWorkspace(
    IVaultStore vaultStore,
    VaultDbContextFactory contextFactory,
    ILogger<DocumentWorkspace> logger) : BackgroundService
{
    /// <summary>Long enough that Word's rename dance has finished, short enough to feel immediate.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>Word holds an exclusive lock while it saves. This is how long we wait it out.</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(8);

    private const string WorkingFolderName = ".travail";

    private readonly ConcurrentDictionary<Guid, CheckedOut> _open = new();

    public static string WorkingRoot(OpenVault vault) =>
        Path.Combine(vault.Paths.Root, WorkingFolderName);

    /// <summary>
    /// Decrypts the document into the working folder and starts watching it. Checking out a document
    /// that is already out is not an error — it returns the same path, so a second double-click just
    /// brings the window forward.
    /// </summary>
    public async Task<string> CheckOutAsync(
        Guid vaultId,
        Document document,
        CancellationToken cancellationToken)
    {
        if (_open.TryGetValue(document.Id, out var already))
        {
            return already.Path;
        }

        var vault = vaultStore.Get(vaultId);
        var directory = Path.Combine(WorkingRoot(vault), document.Id.ToString());
        Directory.CreateDirectory(directory);

        // The document id, not the file name, is the folder: two dossiers both holding
        // « conclusions.docx » must not land on the same working path.
        var path = Path.Combine(directory, SafeName(document.FileName));
        var reference = new BlobReference(document.BlobSha256, document.SizeBytes);

        await using (var source = vault.Blobs.OpenRead(reference))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var info = new FileInfo(path);
        _open[document.Id] = new CheckedOut(
            vaultId,
            document.Id,
            path,
            document.BlobSha256,
            info.Length,
            info.LastWriteTimeUtc);

        logger.LogInformation("Document {DocumentId} checked out to {Path}.", document.Id, path);

        return path;
    }

    /// <summary>Takes a last look, puts the changes away and removes the working copy.</summary>
    public async Task CheckInAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!_open.TryRemove(documentId, out var entry))
        {
            return;
        }

        await SyncAsync(entry, cancellationToken);
        Discard(entry);
    }

    public IReadOnlyList<CheckedOutStatus> Status() =>
        [.. _open.Values.Select(entry => new CheckedOutStatus(entry.DocumentId, entry.Path))];

    /// <summary>
    /// Files left in the working folder by a crash. Reported, never deleted on sight: the copy on
    /// disk may hold work the coffre has never seen.
    /// </summary>
    public IReadOnlyList<AbandonedFile> Abandoned(Guid vaultId)
    {
        var root = WorkingRoot(vaultStore.Get(vaultId));
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(root)
                .Where(directory => !_open.ContainsKey(ParseId(directory)))
                .SelectMany(directory => Directory.EnumerateFiles(directory))
                .Where(file => !IsScratch(file))
                .Select(file => new AbandonedFile(
                    ParseId(Path.GetDirectoryName(file)!),
                    Path.GetFileName(file),
                    new FileInfo(file).LastWriteTimeUtc)),
        ];
    }

    /// <summary>Reintegrates an abandoned file, or throws it away, on the user's explicit say-so.</summary>
    public async Task ResolveAbandonedAsync(
        Guid vaultId,
        Guid documentId,
        bool keep,
        CancellationToken cancellationToken)
    {
        var root = WorkingRoot(vaultStore.Get(vaultId));
        var directory = Path.Combine(root, documentId.ToString());

        if (!Directory.Exists(directory))
        {
            return;
        }

        if (keep)
        {
            var file = Directory.EnumerateFiles(directory).FirstOrDefault(candidate => !IsScratch(candidate));

            if (file is not null)
            {
                await StoreAsync(vaultId, documentId, file, cancellationToken);
            }
        }

        TryDeleteDirectory(directory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var entry in _open.Values)
            {
                try
                {
                    await SyncAsync(entry, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A save that cannot be read yet is normal, not an error worth stopping the loop
                    // for: the next tick, 1.5 seconds later, will pick it up.
                    logger.LogDebug(exception, "Document {DocumentId} not readable yet.", entry.DocumentId);
                }
            }
        }
    }

    /// <summary>
    /// Puts the working copy away if, and only if, its bytes differ from what the coffre holds. The
    /// cheap checks come first so the common case — an open file nobody is typing in — costs a stat.
    /// </summary>
    private async Task SyncAsync(CheckedOut entry, CancellationToken cancellationToken)
    {
        var info = new FileInfo(entry.Path);

        if (!info.Exists)
        {
            return;
        }

        if (info.Length == entry.LastLength && info.LastWriteTimeUtc == entry.LastWriteUtc)
        {
            return;
        }

        if (!await WaitForUnlockedAsync(entry.Path, cancellationToken))
        {
            return;
        }

        var sha = await HashAsync(entry.Path, cancellationToken);

        // Word rewrites the whole file on every save, so the timestamp moves even when nothing was
        // typed. Hashing is what stops a version being created for each of those.
        if (string.Equals(sha, entry.LastSha256, StringComparison.OrdinalIgnoreCase))
        {
            _open[entry.DocumentId] = entry with
            {
                LastLength = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
            };

            return;
        }

        await StoreAsync(entry.VaultId, entry.DocumentId, entry.Path, cancellationToken);

        var updated = new FileInfo(entry.Path);
        _open[entry.DocumentId] = entry with
        {
            LastSha256 = sha,
            LastLength = updated.Length,
            LastWriteUtc = updated.LastWriteTimeUtc,
        };

        logger.LogInformation("Document {DocumentId} reintegrated from {Path}.", entry.DocumentId, entry.Path);
    }

    private async Task StoreAsync(
        Guid vaultId,
        Guid documentId,
        string path,
        CancellationToken cancellationToken)
    {
        var vault = vaultStore.Get(vaultId);

        await using var database = contextFactory.Create(vaultId);

        var document = await database.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);

        if (document is null)
        {
            return;
        }

        BlobReference blob;
        await using (var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            blob = await vault.Blobs.PutAsync(content, cancellationToken);
        }

        var previous = new BlobReference(document.BlobSha256, document.SizeBytes);

        document.BlobSha256 = blob.Sha256;
        document.SizeBytes = blob.SizeBytes;
        document.Version += 1;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        // The blob store is content-addressed, so the old bytes are only garbage once nothing points
        // at them. Two documents can legitimately share a blob after a duplicate upload.
        if (previous.Sha256 != blob.Sha256)
        {
            var stillReferenced = await database.Documents.AnyAsync(
                candidate => candidate.BlobSha256 == previous.Sha256, cancellationToken);

            if (!stillReferenced)
            {
                vault.Blobs.Delete(previous);
            }
        }
    }

    /// <summary>
    /// Word holds the file exclusively while it writes. Reading through that produces a truncated
    /// document, so the answer is to wait rather than to read what happens to be there.
    /// </summary>
    private static async Task<bool> WaitForUnlockedAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + LockTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var probe = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(200, cancellationToken);
            }
        }

        return false;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var content = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = await SHA256.HashDataAsync(content, cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void Discard(CheckedOut entry) => TryDeleteDirectory(Path.GetDirectoryName(entry.Path)!);

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            // A file still held by Word cannot be deleted, and that is not worth failing a request
            // over: it will be offered as abandoned on the next launch.
            logger.LogWarning(exception, "Could not remove working folder {Directory}.", directory);
        }
    }

    /// <summary>Word's own lock and scratch files, which are never the document.</summary>
    private static bool IsScratch(string path)
    {
        var name = Path.GetFileName(path);

        return name.StartsWith("~$", StringComparison.Ordinal) ||
               name.StartsWith('.') ||
               name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static Guid ParseId(string directory) =>
        Guid.TryParse(Path.GetFileName(directory), out var id) ? id : Guid.Empty;

    private static string SafeName(string fileName)
    {
        var cleaned = new string([.. Path.GetFileName(fileName)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)]);

        return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned;
    }

    private sealed record CheckedOut(
        Guid VaultId,
        Guid DocumentId,
        string Path,
        string LastSha256,
        long LastLength,
        DateTime LastWriteUtc);
}

public sealed record CheckedOutStatus(Guid DocumentId, string Path);

public sealed record AbandonedFile(Guid DocumentId, string FileName, DateTime ModifiedUtc);
