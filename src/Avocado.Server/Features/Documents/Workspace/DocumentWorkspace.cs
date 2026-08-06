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

        // A leftover copy of the same document, still held by the reader that opened it last time,
        // must not turn « ouvrir » into a failure. When the bytes already match, reuse the file.
        if (File.Exists(path))
        {
            var existing = await TryHashAsync(path, cancellationToken);

            if (existing is null || string.Equals(existing, document.BlobSha256, StringComparison.OrdinalIgnoreCase))
            {
                Register(vaultId, document, path);
                return path;
            }
        }

        await using (var source = vault.Blobs.OpenRead(reference))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        Register(vaultId, document, path);
        logger.LogInformation("Document {DocumentId} checked out to {Path}.", document.Id, path);

        return path;
    }

    private void Register(Guid vaultId, Document document, string path)
    {
        var info = new FileInfo(path);

        _open[document.Id] = new CheckedOut(
            vaultId, document.Id, path, document.BlobSha256, info.Length, info.LastWriteTimeUtc);
    }

    private static async Task<string?> TryHashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await HashAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes a last look, puts the changes away and removes the working copy.
    /// <para>
    /// The removal is retried, because the application she used to open the file often still holds it
    /// for a second or two after its window closes. If it is still held after that the folder stays,
    /// and is swept away at the next launch rather than reported as an error now: the bytes are
    /// already safe in the coffre, so nothing is at stake.
    /// </para>
    /// </summary>
    public async Task CheckInAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!_open.TryRemove(documentId, out var entry))
        {
            return;
        }

        await SyncAsync(entry, cancellationToken);
        await DiscardAsync(Path.GetDirectoryName(entry.Path)!, cancellationToken);
    }

    public IReadOnlyList<CheckedOutStatus> Status() =>
        [.. _open.Values.Select(entry => new CheckedOutStatus(entry.DocumentId, entry.Path))];

    /// <summary>
    /// What is left in the working folder, once everything that already matches the coffre has been
    /// swept away.
    ///
    /// <para>Most leftovers are not abandoned work at all: they are copies that were reintegrated
    /// correctly and whose folder could not be removed because the reader still held the file. Those
    /// hash identically to what the coffre holds and are deleted here, silently, which is what makes
    /// the folder clean itself up.</para>
    ///
    /// <para>What remains — bytes the coffre has never seen — is reported and never deleted on sight.
    /// A crash must not silently discard an afternoon's drafting.</para>
    /// </summary>
    public async Task<IReadOnlyList<AbandonedFile>> AbandonedAsync(
        Guid vaultId,
        CancellationToken cancellationToken)
    {
        var root = WorkingRoot(vaultStore.Get(vaultId));

        if (!Directory.Exists(root))
        {
            return [];
        }

        await using var database = contextFactory.Create(vaultId);
        var abandoned = new List<AbandonedFile>();

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var documentId = ParseId(directory);

            if (_open.ContainsKey(documentId))
            {
                continue;
            }

            var file = Directory.EnumerateFiles(directory).FirstOrDefault(candidate => !IsScratch(candidate));

            if (file is null)
            {
                await DiscardAsync(directory, cancellationToken);
                continue;
            }

            var stored = await database.Documents
                .AsNoTracking()
                .Where(candidate => candidate.Id == documentId)
                .Select(candidate => candidate.BlobSha256)
                .FirstOrDefaultAsync(cancellationToken);

            var sha = await TryHashAsync(file, cancellationToken);

            if (sha is null)
            {
                // Still held by something. Leave it alone; the next look will find it released.
                continue;
            }

            if (stored is null || string.Equals(stored, sha, StringComparison.OrdinalIgnoreCase))
            {
                await DiscardAsync(directory, cancellationToken);
                continue;
            }

            abandoned.Add(new AbandonedFile(documentId, Path.GetFileName(file), new FileInfo(file).LastWriteTimeUtc));
        }

        return abandoned;
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

        await DiscardAsync(directory, cancellationToken);
    }

    /// <summary>
    /// A clean shutdown puts every open file away and empties the working folder, so the plaintext
    /// does not outlive the session. A hard kill cannot run this, which is exactly why the sweep in
    /// <see cref="AbandonedAsync"/> exists.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var documentId in _open.Keys.ToList())
        {
            try
            {
                await CheckInAsync(documentId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not put document {DocumentId} away on shutdown.", documentId);
            }
        }

        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sweep once at startup rather than waiting for someone to open the Documents tab. A hard
        // kill cannot run StopAsync, so without this the plaintext left by the last session would sit
        // in the coffre folder until she happened to look at a dossier's documents.
        await SweepAsync(stoppingToken);

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
    /// Runs the same reconciliation the Documents tab asks for, for every vault currently open.
    /// Anything genuinely modified survives and is reported the first time a screen asks.
    /// </summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Desktop: one vault, and every id resolves to it. TryGet rather than Get so a locked
            // vault at startup is a no-op rather than a background-service crash.
            if (!vaultStore.TryGet(Guid.Empty, out var vault) || vault is null)
            {
                logger.LogInformation("Working folder not swept: no vault open yet.");
                return;
            }

            var root = WorkingRoot(vault);
            var before = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
            var left = await AbandonedAsync(vault.Id, cancellationToken);

            // Logged either way. This folder holds plaintext, so « how many were there and how many
            // are left » is the one line worth having in the journal after a crash.
            logger.LogInformation(
                "Working folder swept: {Before} folder(s) found, {Left} awaiting a decision.",
                before, left.Count);

            // Leave nothing behind at all when there is nothing left to hold.
            if (left.Count == 0 && Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0)
            {
                await DiscardAsync(root, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Startup sweep of the working folder failed.");
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

    /// <summary>
    /// Four attempts over about a second. Word and most PDF readers release the handle shortly after
    /// their window closes, and retrying costs nothing next to asking the user to try again.
    /// </summary>
    private async Task DiscardAsync(string directory, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 3)
                {
                    // Still held. Not worth failing a request over: the bytes are already in the
                    // coffre, and the next launch sweeps the folder away once the handle is released.
                    logger.LogWarning(exception, "Could not remove working folder {Directory}.", directory);
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }
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
