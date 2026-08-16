using System.Security.Cryptography;
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
    ILogger<MatterCheckoutService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string RootFor(Guid vaultId) => Path.Combine(working.For(vaultId), "dossiers");

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

            if (checkout is null || !Directory.Exists(checkout.FolderPath))
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

    /// <summary>« J'ai terminé »: a last sync, deletions included, then the folder goes.</summary>
    public async Task<IReadOnlyList<CheckoutChange>> CloseAsync(Guid matterId, CancellationToken cancellationToken)
    {
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

        var open = await database.MatterCheckouts.Select(checkout => checkout.MatterId)
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

    private static string? FolderOf(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index <= 0 ? null : relativePath[..index];
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
