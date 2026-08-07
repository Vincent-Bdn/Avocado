namespace Avocado.Vault.Blobs;

/// <param name="Sha256">Hex SHA-256 of the <em>plaintext</em>. This is what a Document row stores.</param>
/// <param name="SizeBytes">Plaintext length.</param>
public readonly record struct BlobReference(string Sha256, long SizeBytes);

/// <summary>
/// Encrypted document storage. Scans and PDFs live here rather than in the database: streaming a 50 MB
/// file matters, and per-file blobs keep backup granularity sane.
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Encrypts and stores <paramref name="content"/>. Identical content stores once, the second call
    /// returns the existing reference without rewriting.
    /// </summary>
    Task<BlobReference> PutAsync(Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens a decrypting, forward-only stream. Chunk-authenticated, so a truncated or
    /// tampered file fails partway through rather than returning plausible garbage.</summary>
    Stream OpenRead(BlobReference blob);

    bool Exists(BlobReference blob);

    bool Delete(BlobReference blob);
}
