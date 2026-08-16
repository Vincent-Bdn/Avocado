using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Workspace;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Checkout;

/// <summary>
/// Opens a whole dossier as a folder, keeps it and the vault in step while she works, and puts it
/// away again.
///
/// <para>The decision layer lives in <see cref="CheckoutReconciler"/> and
/// <see cref="CheckoutResumptionCheck"/>, which know nothing about disks. This is the part that
/// actually decrypts, hashes and deletes, and it is deliberately thin: everything that could be
/// decided wrongly was decided there, where it could be tested without a filesystem.</para>
/// </summary>
public sealed class MatterCheckoutService(
    IVaultStore vaults,
    VaultDbContextFactory contexts,
    WorkingDirectory working,
    Mails.Infrastructure.MailIngest mails,
    ILogger<MatterCheckoutService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Beside the per-document workspace, never inside it. DocumentWorkspace deletes any directory in
    /// its own root whose name is not a document id, so a dossier folder living there was recursively
    /// removed as an orphan at the next startup, along with everything dropped into it.
    /// </summary>
    private string RootFor(Guid vaultId) => working.DossiersFor(vaultId);

    /// <summary>
    /// Decrypts every document of a dossier into a folder and records what was written.
    ///
    /// <para>Reopening an already open dossier returns the same folder rather than a second copy of
    /// it, since two folders holding the same documents makes "which one is right" unanswerable.</para>
    /// </summary>
    public async Task<MatterCheckout> OpenAsync(Guid matterId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var vault = vaults.Get(Guid.Empty);
            await using var database = contexts.Create(vault.Id);

            var existing = await database.MatterCheckouts
                .FirstOrDefaultAsync(checkout => checkout.MatterId == matterId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null && Directory.Exists(existing.FolderPath))
            {
                return existing;
            }

            var matter = await database.Matters
                .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new VaultException("Ce dossier n'existe pas.");

            var documents = await database.Documents
                .Where(document => document.MatterId == matterId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var folder = Path.Combine(RootFor(vault.Id), FolderName(matter.Reference, matter.Name));
            Directory.CreateDirectory(folder);

            var manifest = new List<BorrowedFile>();

            foreach (var document in documents)
            {
                var relative = RelativePathFor(document, manifest);
                var destination = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                var source = vault.Blobs.OpenRead(new BlobReference(document.BlobSha256, document.SizeBytes));
                await using (source.ConfigureAwait(false))
                {
                    var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                    await using (file.ConfigureAwait(false))
                    {
                        await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                    }
                }

                manifest.Add(new BorrowedFile(document.Id, relative, document.BlobSha256, document.SizeBytes));
            }

            var checkout = existing ?? new MatterCheckout { MatterId = matterId };
            checkout.FolderPath = folder;
            checkout.Manifest = JsonSerializer.Serialize(manifest);
            checkout.OpenedAt = DateTimeOffset.UtcNow;
            checkout.SyncedAt = DateTimeOffset.UtcNow;

            if (existing is null)
            {
                database.MatterCheckouts.Add(checkout);
            }

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Opened {Count} documents of {Matter} into {Folder}.", manifest.Count, matter.Reference, folder);
            return checkout;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>What the folder looks like now, against what was handed over.</summary>
    public async Task<IReadOnlyList<CheckoutChange>> InspectAsync(
        MatterCheckout checkout,
        CancellationToken cancellationToken)
    {
        var borrowed = JsonSerializer.Deserialize<List<BorrowedFile>>(checkout.Manifest) ?? [];
        return CheckoutReconciler.Compare(borrowed, await ScanAsync(checkout.FolderPath, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Writes back everything that changed, then updates the manifest so the next pass compares
    /// against what is now true.
    ///
    /// <para>Deletions are applied only when <paramref name="applyDeletions"/> says so. While she is
    /// working, they are not: a file that vanishes mid-afternoon is far more likely to be Word
    /// rewriting it than a decision, and the vault must not follow. They are confirmed at the end.</para>
    /// </summary>
    public async Task<IReadOnlyList<CheckoutChange>> SyncAsync(
        Guid matterId,
        bool applyDeletions,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var vault = vaults.Get(Guid.Empty);
            await using var database = contexts.Create(vault.Id);

            var checkout = await database.MatterCheckouts
                .FirstOrDefaultAsync(candidate => candidate.MatterId == matterId, cancellationToken)
                .ConfigureAwait(false);

            if (checkout is null || checkout.AwaitingDecision || !Directory.Exists(checkout.FolderPath))
            {
                return [];
            }

            var changes = await InspectAsync(checkout, cancellationToken).ConfigureAwait(false);
            var manifest = new List<BorrowedFile>();

            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (change.Kind)
                {
                    case CheckoutChangeKind.Unchanged:
                        manifest.Add(new BorrowedFile(change.DocumentId, change.RelativePath, change.Sha256!, change.SizeBytes));
                        break;

                    case CheckoutChangeKind.Modified:
                        await StoreAsync(vault, database, change, checkout.FolderPath, cancellationToken).ConfigureAwait(false);
                        manifest.Add(new BorrowedFile(change.DocumentId, change.RelativePath, change.Sha256!, change.SizeBytes));
                        break;

                    case CheckoutChangeKind.Renamed:
                        // Contents unchanged, so nothing to store: only the name and its folder move.
                        var renamed = await database.Documents.FindAsync([change.DocumentId], cancellationToken).ConfigureAwait(false);
                        if (renamed is not null)
                        {
                            renamed.FileName = Path.GetFileName(change.RelativePath);
                            renamed.Folder = FolderOf(change.RelativePath);
                            renamed.UpdatedAt = DateTimeOffset.UtcNow;
                        }

                        manifest.Add(new BorrowedFile(change.DocumentId, change.RelativePath, change.Sha256!, change.SizeBytes));
                        break;

                    case CheckoutChangeKind.Added:
                        var created = await AddAsync(vault, database, matterId, change, checkout.FolderPath, cancellationToken).ConfigureAwait(false);
                        manifest.Add(new BorrowedFile(created, change.RelativePath, change.Sha256!, change.SizeBytes));

                        // Dragging a message out of Outlook into this folder is the whole email
                        // feature. A dropped .msg becomes a journal entry rather than an opaque file,
                        // and its attachments become pièces of their own.
                        await IngestMailAsync(vault, database, matterId, created, checkout.FolderPath, change, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case CheckoutChangeKind.Deleted when applyDeletions:
                        var removed = await database.Documents.FindAsync([change.DocumentId], cancellationToken).ConfigureAwait(false);
                        if (removed is not null)
                        {
                            database.Documents.Remove(removed);
                        }

                        break;

                    case CheckoutChangeKind.Deleted:
                        // Kept in the manifest so it is still reported at the end rather than
                        // forgotten by the pass that declined to act on it.
                        manifest.Add(new BorrowedFile(change.DocumentId, change.RelativePath, change.Sha256!, change.SizeBytes));
                        break;
                }
            }

            checkout.Manifest = JsonSerializer.Serialize(manifest);
            checkout.SyncedAt = DateTimeOffset.UtcNow;

            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return changes;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Her answer to « le contenu a changé pendant qu'Avocado était fermé ».
    ///
    /// <para><paramref name="keepFolder"/> takes what is on disk as the truth and writes it back,
    /// which is what someone means when they worked in the folder with Avocado closed. Otherwise the
    /// folder is thrown away and written again from the vault, which is what someone means when the
    /// change was not theirs, or was a mistake.</para>
    ///
    /// <para>Deletions are not applied either way. Removing a pièce is a decision that belongs to
    /// « J'ai terminé », where it is listed, and never to a recovery prompt answered in a hurry on a
    /// morning that has already gone wrong.</para>
    /// </summary>
    public async Task ResolveAsync(Guid matterId, bool keepFolder, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var vault = vaults.Get(Guid.Empty);
            await using var database = contexts.Create(vault.Id);

            var checkout = await database.MatterCheckouts
                .FirstOrDefaultAsync(candidate => candidate.MatterId == matterId, cancellationToken)
                .ConfigureAwait(false);

            if (checkout is null)
            {
                return;
            }

            checkout.AwaitingDecision = false;
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (keepFolder)
        {
            // Deletions included, and this is the one place besides « J'ai terminé » where they are.
            // She was shown the list and answered that the folder is what counts; leaving the removed
            // ones in the vault would mean the screen reporting them as « supprimé » on every pass
            // afterwards, with no way to make it stop short of closing the dossier.
            await SyncAsync(matterId, applyDeletions: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Discarding means handing the folder back exactly as the vault has it, which is what Open
        // does from scratch.
        await using (var database = contexts.Create(vaults.Get(Guid.Empty).Id))
        {
            var checkout = await database.MatterCheckouts
                .FirstOrDefaultAsync(candidate => candidate.MatterId == matterId, cancellationToken)
                .ConfigureAwait(false);

            if (checkout is not null && !TryRemove(checkout.FolderPath))
            {
                throw new VaultException(
                    "Un fichier du dossier est encore ouvert dans une autre application. Fermez-le, puis réessayez.");
            }
        }

        await OpenAsync(matterId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// « J'ai terminé »: a last sync, deletions included, then the folder goes.
    ///
    /// <para>Refused while the folder is waiting on her answer about changes made offline. Closing
    /// then would delete a folder whose contents were never written back, which is the one outcome
    /// the whole resumption path exists to prevent.</para>
    /// </summary>
    public async Task<IReadOnlyList<CheckoutChange>> CloseAsync(Guid matterId, CancellationToken cancellationToken)
    {
        await using (var guard = contexts.Create(vaults.Get(Guid.Empty).Id))
        {
            if (await guard.MatterCheckouts
                    .AnyAsync(candidate => candidate.MatterId == matterId && candidate.AwaitingDecision, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new VaultException(
                    "Ce dossier attend que vous disiez quoi faire des modifications faites hors d'Avocado.");
            }
        }

        var changes = await SyncAsync(matterId, applyDeletions: true, cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var vault = vaults.Get(Guid.Empty);
            await using var database = contexts.Create(vault.Id);

            var checkout = await database.MatterCheckouts
                .FirstOrDefaultAsync(candidate => candidate.MatterId == matterId, cancellationToken)
                .ConfigureAwait(false);

            if (checkout is null)
            {
                return changes;
            }

            if (TryRemove(checkout.FolderPath))
            {
                database.MatterCheckouts.Remove(checkout);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Something still holds a file. The row stays, so the next launch finds the folder,
                // compares it and reopens rather than treating it as debris.
                logger.LogWarning("Folder {Folder} could not be removed; it stays open.", checkout.FolderPath);
            }

            return changes;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// On the way out: everything is written back and every folder removed. Anything still held stays,
    /// and is picked up at the next launch by the resumption check rather than forced.
    /// </summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        var vault = vaults.Get(Guid.Empty);
        await using var database = contexts.Create(vault.Id);

        // Anything waiting on her answer is left exactly where it is. Closing it would run a sync
        // that is deliberately held, find nothing to write, and then delete the folder, which is the
        // work we held it for. Shutting down is not an answer to a question nobody read.
        var open = await database.MatterCheckouts
            .Where(checkout => !checkout.AwaitingDecision)
            .Select(checkout => checkout.MatterId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var matterId in open)
        {
            try
            {
                await CloseAsync(matterId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not close {Matter} on shutdown.", matterId);
            }
        }
    }

    /// <summary>
    /// At startup, for each folder still on disk. Never deletes: see
    /// <see cref="CheckoutResumptionCheck"/> for why tidying up here would destroy work.
    /// </summary>
    public async Task<IReadOnlyList<(MatterCheckout Checkout, CheckoutResumption Resumption)>> ResumeAsync(
        CancellationToken cancellationToken)
    {
        var vault = vaults.Get(Guid.Empty);
        await using var database = contexts.Create(vault.Id);

        var results = new List<(MatterCheckout, CheckoutResumption)>();

        foreach (var checkout in await database.MatterCheckouts.ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            var borrowed = JsonSerializer.Deserialize<List<BorrowedFile>>(checkout.Manifest) ?? [];
            var exists = Directory.Exists(checkout.FolderPath);

            var resumption = CheckoutResumptionCheck.Assess(
                exists,
                borrowed,
                exists ? await ScanAsync(checkout.FolderPath, cancellationToken).ConfigureAwait(false) : []);

            if (resumption.Verdict is ResumeVerdict.Completed)
            {
                database.MatterCheckouts.Remove(checkout);
                continue;
            }

            // Held until she answers. The sweep would otherwise write the folder into the vault within
            // five seconds, which would make asking a formality performed after the fact.
            checkout.AwaitingDecision = resumption.NeedsAsking;

            results.Add((checkout, resumption));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return results;
    }

    private static async Task<IReadOnlyList<FolderFile>> ScanAsync(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var files = new List<FolderFile>();

        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (CheckoutReconciler.IsDebris(relative))
            {
                continue;
            }

            try
            {
                files.Add(new FolderFile(relative, await HashAsync(path, cancellationToken).ConfigureAwait(false), new FileInfo(path).Length));
            }
            catch (IOException)
            {
                // Held open by whatever is editing it. Skipping means it counts as unchanged this
                // pass, which is right: it is mid-write, and its final contents are not knowable yet.
            }
        }

        return files;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Files a dropped message as a journal entry and stores each of its attachments as a document of
    /// its own, so the client's PDF is findable in the dossier rather than sealed inside a container
    /// only Outlook opens.
    ///
    /// <para>The attachments are written straight to the vault and never to the folder. Putting them
    /// on disk would make them newcomers on the next pass, and the mail would grow a copy of its own
    /// attachments every five seconds.</para>
    /// </summary>
    private async Task IngestMailAsync(
        OpenVault vault,
        AvocadoDbContext database,
        Guid matterId,
        Guid documentId,
        string folder,
        CheckoutChange change,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, change.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        var attachments = await mails.RecordAsync(database, matterId, documentId, path, cancellationToken)
            .ConfigureAwait(false);

        if (attachments is null)
        {
            return;
        }

        foreach (var attachment in attachments)
        {
            using var content = new MemoryStream(attachment.Content);
            var reference = await vault.Blobs.PutAsync(content, cancellationToken).ConfigureAwait(false);

            database.Documents.Add(new Document
            {
                MatterId = matterId,
                BlobSha256 = reference.Sha256,
                SizeBytes = reference.SizeBytes,
                FileName = attachment.FileName,
                MimeType = attachment.ContentType,
                Folder = FolderOf(change.RelativePath),
            });
        }
    }

    private static async Task StoreAsync(
        OpenVault vault,
        AvocadoDbContext database,
        CheckoutChange change,
        string folder,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents.FindAsync([change.DocumentId], cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        var reference = await PutAsync(vault, folder, change.RelativePath, cancellationToken).ConfigureAwait(false);

        document.BlobSha256 = reference.Sha256;
        document.SizeBytes = reference.SizeBytes;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document.Version++;
    }

    private static async Task<Guid> AddAsync(
        OpenVault vault,
        AvocadoDbContext database,
        Guid matterId,
        CheckoutChange change,
        string folder,
        CancellationToken cancellationToken)
    {
        var reference = await PutAsync(vault, folder, change.RelativePath, cancellationToken).ConfigureAwait(false);

        var document = new Document
        {
            MatterId = matterId,
            BlobSha256 = reference.Sha256,
            SizeBytes = reference.SizeBytes,
            FileName = Path.GetFileName(change.RelativePath),
            Folder = FolderOf(change.RelativePath),
        };

        database.Documents.Add(document);
        return document.Id;
    }

    private static async Task<BlobReference> PutAsync(
        OpenVault vault,
        string folder,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await vault.Blobs.PutAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The document's own grouping becomes a real subfolder, and comes back the same way.</summary>
    private static string RelativePathFor(Document document, List<BorrowedFile> taken)
    {
        // Segment, not Sanitise: a file name is one name. A document called « conclusions 1/2.docx »
        // must not quietly become a folder.
        var name = Segment(document.FileName is { Length: > 0 } given ? given : $"document-{document.Id:N}");
        var candidate = string.IsNullOrWhiteSpace(document.Folder) ? name : $"{Sanitise(document.Folder!)}/{name}";

        // Two documents may legitimately carry the same name. On disk they cannot.
        var unique = candidate;
        var suffix = 2;

        while (taken.Any(file => string.Equals(file.RelativePath, unique, StringComparison.OrdinalIgnoreCase)))
        {
            unique = Path.ChangeExtension(candidate, null) + $" ({suffix++})" + Path.GetExtension(candidate);
        }

        return unique;
    }

    /// <summary>
    /// The grouping a document inherits from where it sits on disk.
    ///
    /// <para>Normalised, because a folder name is a string and two strings that look identical are not
    /// necessarily equal. « Pièces » is one code point on Windows and two on macOS, or after a paste
    /// from a browser, and a trailing space is invisible in Explorer. Left alone, one folder became
    /// two groups with the same name, and a document went into whichever the last sweep happened to
    /// produce. Unicode composition and trimmed segments are what make « the same folder » mean the
    /// same thing twice running.</para>
    /// </summary>
    private static string? FolderOf(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        if (index <= 0)
        {
            return null;
        }

        var segments = relativePath[..index]
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Normalize(NormalizationForm.FormC).Trim())
            .Where(segment => segment.Length > 0)
            .ToList();

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>
    /// One directory name, so the separator goes too. « Dupont c/ Martin » is how half of French
    /// litigation is named, and left alone it silently creates a « Dupont c » folder containing a
    /// « Martin » folder, which is not what anyone asked for and is hard to notice.
    /// </summary>
    private static string FolderName(string reference, string title) =>
        Segment($"{reference} {title}".Trim()) is { Length: > 0 } name ? name : "dossier";

    /// <summary>A single name: every invalid character, and the separators, become a hyphen.</summary>
    private static string Segment(string value) =>
        string.Concat(value.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '-' : c)).Trim();

    /// <summary>A relative path, where a forward slash is structure and everything else is a name.</summary>
    private static string Sanitise(string value) =>
        string.Join('/', value.Replace('\\', '/').Split('/').Select(Segment));

    private static bool TryRemove(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
