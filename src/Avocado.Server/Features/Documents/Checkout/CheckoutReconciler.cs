namespace Avocado.Server.Features.Documents.Checkout;

/// <param name="DocumentId">The document this file was decrypted from. Null for something she added.</param>
/// <param name="Sha256">Of the plaintext, which is what the blob store already keys on.</param>
public readonly record struct BorrowedFile(Guid? DocumentId, string RelativePath, string Sha256, long SizeBytes);

/// <summary>What a file in the folder looks like now.</summary>
public readonly record struct FolderFile(string RelativePath, string Sha256, long SizeBytes);

public enum CheckoutChangeKind
{
    Unchanged,
    Modified,
    Added,

    /// <summary>Same contents under a different name. Not a delete plus an add, see the reconciler.</summary>
    Renamed,

    Deleted,
}

/// <param name="PreviousPath">Only for a rename, so the review can say « X devenu Y ».</param>
public sealed record CheckoutChange(
    CheckoutChangeKind Kind,
    string RelativePath,
    Guid? DocumentId,
    string? Sha256,
    long SizeBytes,
    string? PreviousPath = null);

/// <summary>
/// Works out what happened to a dossier's folder while it was borrowed.
///
/// <para>This is the whole difficulty of the feature. Decrypting files is easy; deciding what it means
/// when one of them is no longer where it was is not, and every wrong answer is somebody's pièce.</para>
///
/// <para><b>Renames are detected rather than inferred as a delete and an add.</b> Blobs are
/// content-addressed, so identical contents under a new name is a fact we can see, not a guess. Treating
/// it as a delete plus an add would work, and would silently drop the document's classification, its
/// pièce number and its place in the journal, which is most of what Avocado knows about it.</para>
///
/// <para><b>Nothing is decided here.</b> The reconciler produces a list to show her; the destructive
/// half of it is confirmed at return time, per file. An accidental delete in Explorer then costs one
/// click, which is the difference between a folder that behaves like a network drive and one that
/// behaves like a filing system for material she cannot re-obtain.</para>
/// </summary>
public static class CheckoutReconciler
{
    /// <summary>
    /// Debris every editor and file manager leaves behind. Word writes a <c>~$</c> lock file beside an
    /// open document, Explorer writes Thumbs.db, the Finder writes .DS_Store. None of it is a pièce,
    /// and offering to file it would train someone to click through the review without reading it.
    /// </summary>
    public static bool IsDebris(string relativePath)
    {
        var name = Path.GetFileName(relativePath);

        return name.StartsWith("~$", StringComparison.Ordinal)
            || name.StartsWith(".~lock.", StringComparison.Ordinal)
            || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".DS_Store", StringComparison.Ordinal)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<CheckoutChange> Compare(
        IReadOnlyList<BorrowedFile> borrowed,
        IReadOnlyList<FolderFile> present)
    {
        var current = present.Where(file => !IsDebris(file.RelativePath)).ToList();

        var changes = new List<CheckoutChange>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var byPath = current.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        // Missing files are held back rather than reported straight away: one of them may turn out to
        // be a rename once the unmatched newcomers are known.
        var missing = new List<BorrowedFile>();

        foreach (var original in borrowed)
        {
            if (!byPath.TryGetValue(original.RelativePath, out var file))
            {
                missing.Add(original);
                continue;
            }

            claimed.Add(file.RelativePath);

            changes.Add(new CheckoutChange(
                file.Sha256 == original.Sha256 ? CheckoutChangeKind.Unchanged : CheckoutChangeKind.Modified,
                file.RelativePath,
                original.DocumentId,
                file.Sha256,
                file.SizeBytes));
        }

        var newcomers = current.Where(file => !claimed.Contains(file.RelativePath)).ToList();

        foreach (var gone in missing)
        {
            // Same contents somewhere else is a rename. Matched by hash and consumed, so two files
            // that happen to be identical cannot both claim the same origin.
            var moved = newcomers.FirstOrDefault(file => file.Sha256 == gone.Sha256);

            if (moved.RelativePath is not null)
            {
                newcomers.Remove(moved);

                changes.Add(new CheckoutChange(
                    CheckoutChangeKind.Renamed,
                    moved.RelativePath,
                    gone.DocumentId,
                    moved.Sha256,
                    moved.SizeBytes,
                    gone.RelativePath));

                continue;
            }

            changes.Add(new CheckoutChange(
                CheckoutChangeKind.Deleted,
                gone.RelativePath,
                gone.DocumentId,
                gone.Sha256,
                gone.SizeBytes));
        }

        foreach (var added in newcomers)
        {
            changes.Add(new CheckoutChange(
                CheckoutChangeKind.Added,
                added.RelativePath,
                null,
                added.Sha256,
                added.SizeBytes));
        }

        return changes;
    }

    /// <summary>What the review screen shows. Unchanged files are not news and are left out.</summary>
    public static IReadOnlyList<CheckoutChange> Notable(IReadOnlyList<CheckoutChange> changes) =>
        changes.Where(change => change.Kind is not CheckoutChangeKind.Unchanged).ToList();
}
