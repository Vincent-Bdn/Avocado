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

    private const string WorkingFolderName = ".working-dir";

    /// <summary>The folder's first name. Swept away at startup so an upgrade leaves nothing behind.</summary>
    private const string FormerWorkingFolderName = ".travail";

    /// <summary>
    /// How long a file has to sit untouched, unlocked and with no Office sidecar before it is put
    /// away on its own. Closing Word or a PDF reader is not an event any application can observe, so
    /// the alternative to this is plaintext that lingers until she remembers to click « terminer ».
    /// </summary>
    private static readonly TimeSpan IdleCheckIn = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<Guid, CheckedOut> _open = new();

    /// <summary>
    /// Folders whose removal was refused because something still held a file. Retried on every tick:
    /// a single second of retrying at check-in is not enough when the reader keeps the handle for as
    /// long as its window is open.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _pendingRemoval = new();

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
        bool watch,
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
                if (watch)
                {
                    Register(vaultId, document, path);
                }

                return path;
            }
        }

        await using (var source = vault.Blobs.OpenRead(reference))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        // A closed dossier opens read-only: the file is decrypted so she can read it or copy from it,
        // and nothing is watched, so nothing can flow back into a dossier whose journal is frozen.
        if (watch)
        {
            Register(vaultId, document, path);
        }
        else
        {
            _pendingRemoval[directory] = 0;
        }

        logger.LogInformation(
            "Document {DocumentId} checked out to {Path} ({Mode}).",
            document.Id, path, watch ? "read-write" : "read-only");

        return path;
    }

    private void Register(Guid vaultId, Document document, string path)
    {
        var info = new FileInfo(path);

        _open[document.Id] = new CheckedOut(
            vaultId, document.Id, path, document.BlobSha256, info.Length, info.LastWriteTimeUtc,
            DateTimeOffset.UtcNow);
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

        return await ReconcileAsync(vaultId, root, cancellationToken);
    }

    private async Task<IReadOnlyList<AbandonedFile>> ReconcileAsync(
        Guid vaultId,
        string root,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        // The folders are read first and the database asked once, rather than a query per folder
        // inside the loop. One statement is easier to reason about than n, and it keeps the read off
        // the loop that is also deleting directories.
        var folders = Directory.EnumerateDirectories(root).ToList();

        if (folders.Count == 0)
        {
            return [];
        }

        var wanted = folders.Select(ParseId).Where(id => id != Guid.Empty).ToList();

        await using var database = contextFactory.Create(vaultId);
        database.Database.SetCommandTimeout(TimeSpan.FromSeconds(15));

        var storedByDocument = await database.Documents
            .AsNoTracking()
            .Where(candidate => wanted.Contains(candidate.Id))
            .Select(candidate => new { candidate.Id, candidate.BlobSha256 })
            .ToDictionaryAsync(row => row.Id, row => row.BlobSha256, cancellationToken);

        var abandoned = new List<AbandonedFile>();

        foreach (var directory in folders)
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

            storedByDocument.TryGetValue(documentId, out var stored);

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
        //
        // Logged unconditionally. This service is the only thing in the application that writes
        // plaintext to disk, so « it started, and here is what it found » is a line worth having.
        // Off the startup path deliberately. This runs on a background thread while the host is
        // still wiring itself up and the renderer is firing its first requests, and the sweep is not
        // urgent: one tick's delay costs nothing and keeps it clear of that contention.
        await Task.Delay(PollInterval, stoppingToken);

        logger.LogInformation("Document workspace: {Summary}", await SweepAsync(stoppingToken));

        using var timer = new PeriodicTimer(PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var entry in _open.Values)
            {
                try
                {
                    await SyncAsync(entry, stoppingToken);
                    await CheckInIfIdleAsync(entry, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A save that cannot be read yet is normal, not an error worth stopping the loop
                    // for: the next tick, 1.5 seconds later, will pick it up.
                    logger.LogDebug(exception, "Document {DocumentId} not readable yet.", entry.DocumentId);
                }
            }

            await RetryRemovalsAsync(stoppingToken);
        }
    }

    private async Task AdoptFormerFolderAsync(OpenVault vault, CancellationToken cancellationToken)
    {
        var former = Path.Combine(vault.Paths.Root, FormerWorkingFolderName);

        if (!Directory.Exists(former))
        {
            return;
        }

        // Reconciling first deletes everything already safe in the coffre, which is most of it.
        var survivors = await ReconcileAsync(vault.Id, former, cancellationToken);
        var root = WorkingRoot(vault);

        foreach (var survivor in survivors)
        {
            var source = Path.Combine(former, survivor.DocumentId.ToString());
            var target = Path.Combine(root, survivor.DocumentId.ToString());

            try
            {
                if (Directory.Exists(source) && !Directory.Exists(target))
                {
                    Directory.CreateDirectory(root);
                    Directory.Move(source, target);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not move {Source} into the renamed working folder.", source);
            }
        }

        await DiscardAsync(former, cancellationToken);

        if (survivors.Count > 0)
        {
            logger.LogInformation(
                "Carried {Count} modified file(s) over from the former '{Former}' folder.",
                survivors.Count, FormerWorkingFolderName);
        }
    }

    /// <summary>
    /// Puts a file away once nothing has touched it for a while.
    ///
    /// <para>A reader closing its window is not an event: no application can be notified of it. So
    /// the three signals that together mean « nobody is working on this » are used instead — the file
    /// is not locked, Word has left no <c>~$</c> sidecar beside it, and its bytes have not changed
    /// since the last time they were put away. All three, held for three minutes.</para>
    ///
    /// <para>The sidecar is what makes this safe with Word. Word does not hold the document itself
    /// exclusively between saves, so a lock check alone would declare an open document idle and
    /// delete the file out from under it; the sidecar exists for exactly as long as the document is
    /// open in Word.</para>
    /// </summary>
    private async Task CheckInIfIdleAsync(CheckedOut entry, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - entry.LastChangeUtc < IdleCheckIn)
        {
            return;
        }

        var directory = Path.GetDirectoryName(entry.Path)!;

        if (Directory.EnumerateFiles(directory, "~$*").Any())
        {
            return;
        }

        if (!IsFree(entry.Path))
        {
            return;
        }

        logger.LogInformation(
            "Document {DocumentId} idle for {Minutes} min and unlocked; putting it away.",
            entry.DocumentId, (int)IdleCheckIn.TotalMinutes);

        await CheckInAsync(entry.DocumentId, cancellationToken);
    }

    private static bool IsFree(string path)
    {
        try
        {
            using var probe = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task RetryRemovalsAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in _pendingRemoval.Keys.ToList())
        {
            if (!Directory.Exists(directory))
            {
                _pendingRemoval.TryRemove(directory, out _);
                continue;
            }

            // A read-only copy is only removed once nothing holds it and no reader left a sidecar.
            if (Directory.EnumerateFiles(directory).Where(file => !IsScratch(file)).Any(file => !IsFree(file)))
            {
                continue;
            }

            if (Directory.EnumerateFiles(directory, "~$*").Any())
            {
                continue;
            }

            await DiscardAsync(directory, cancellationToken);
        }
    }

    /// <summary>
    /// Runs the same reconciliation the Documents tab asks for, for every vault currently open.
    /// Anything genuinely modified survives and is reported the first time a screen asks.
    /// </summary>
    private async Task<string> SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Desktop: one vault, and every id resolves to it. TryGet rather than Get so a locked
            // vault at startup is a no-op rather than a background-service crash.
            if (!vaultStore.TryGet(Guid.Empty, out var vault) || vault is null)
            {
                return "No vault open; working folder not swept.";
            }

            // An older build wrote to `.travail`. Anything it left that still matches the coffre is
            // swept; anything genuinely modified is *moved* into the new folder rather than deleted,
            // so an upgrade cannot be the thing that loses an afternoon's work.
            await AdoptFormerFolderAsync(vault, cancellationToken);

            var root = WorkingRoot(vault);
            var before = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
            var left = await AbandonedAsync(vault.Id, cancellationToken);

            // Leave nothing behind at all when there is nothing left to hold.
            if (left.Count == 0 && Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0)
            {
                await DiscardAsync(root, cancellationToken);
            }

            // This folder is the only place the application writes plaintext, so « what was there and
            // what is left » is the one line worth having in the journal after a crash.
            return $"Working folder: {before} folder(s) found, {left.Count} awaiting a decision.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Startup sweep of the working folder failed.");
            return "Working folder could not be swept; see the warning above.";
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
            LastChangeUtc = DateTimeOffset.UtcNow,
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

                _pendingRemoval.TryRemove(directory, out _);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 3)
                {
                    // Still held. Not worth failing a request over: the bytes are already in the
                    // coffre. It goes on the retry list and comes off it a second and a half later,
                    // for as long as the application runs.
                    logger.LogDebug(exception, "Working folder {Directory} still held; will retry.", directory);
                    _pendingRemoval[directory] = 0;
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

    /// <param name="LastChangeUtc">
    /// When the bytes last differed from what the coffre holds. Not the file's own timestamp: Word
    /// rewrites an untouched document on every save, and that must not read as activity.
    /// </param>
    private sealed record CheckedOut(
        Guid VaultId,
        Guid DocumentId,
        string Path,
        string LastSha256,
        long LastLength,
        DateTime LastWriteUtc,
        DateTimeOffset LastChangeUtc);
}

public sealed record CheckedOutStatus(Guid DocumentId, string Path);

public sealed record AbandonedFile(Guid DocumentId, string FileName, DateTime ModifiedUtc);
