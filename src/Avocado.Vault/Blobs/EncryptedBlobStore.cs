using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Blobs;

/// <summary>
/// Content-addressed encrypted blobs on disk.
/// <para>
/// The file name is <c>HMAC-SHA256(DEK, sha256(plaintext))</c>, not the plaintext hash itself.
/// Deduplication still works, but a directory listing no longer lets anyone confirm "this vault
/// contains this exact document" by hashing a candidate file, the database is encrypted, and the
/// blob folder should not undo that.
/// </para>
/// </summary>
public sealed class EncryptedBlobStore : IBlobStore
{
    private readonly string _rootDirectory;
    private readonly SecretKey _dataKey;

    public EncryptedBlobStore(string rootDirectory, SecretKey dataKey)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _dataKey = dataKey;
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<BlobReference> PutAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var temporaryDirectory = Path.Combine(_rootDirectory, ".tmp");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, Path.GetRandomFileName());

        try
        {
            var (sha256, size) = await WriteEncryptedAsync(content, temporaryPath, cancellationToken)
                .ConfigureAwait(false);

            var reference = new BlobReference(Convert.ToHexString(sha256).ToLowerInvariant(), size);
            var finalPath = PathFor(reference);

            if (File.Exists(finalPath))
            {
                File.Delete(temporaryPath);  // Already stored, deduplicated.
                return reference;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(temporaryPath, finalPath, overwrite: false);
            return reference;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public Stream OpenRead(BlobReference blob)
    {
        var path = PathFor(blob);
        if (!File.Exists(path))
        {
            throw new VaultCorruptedException(
                $"Document blob {blob.Sha256[..12]}… is referenced by the database but missing from the vault.");
        }

        return DecryptingBlobStream.Open(File.OpenRead(path), _dataKey);
    }

    public bool Exists(BlobReference blob) => File.Exists(PathFor(blob));

    public bool Delete(BlobReference blob)
    {
        var path = PathFor(blob);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private async Task<(byte[] Sha256, long Size)> WriteEncryptedAsync(
        Stream content,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(BlobFormat.SaltSize);
        var noncePrefix = RandomNumberGenerator.GetBytes(BlobFormat.NoncePrefixSize);

        using var blobKey = BlobFormat.DeriveBlobKey(_dataKey, salt);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var output = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);

        await using (output.ConfigureAwait(false))
        {
            await output.WriteAsync(BlobFormat.Magic, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(salt, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(noncePrefix, cancellationToken).ConfigureAwait(false);

            long size = 0;
            long chunkIndex = 0;

            // Heap, not stackalloc: this is an async method and the buffer straddles awaits.
            var nonce = new byte[Aead.NonceSize];

            // Turned off for the rest of the file by the first chunk that does not compress. See
            // Compress: one wasted pass over a megabyte tells us what kind of file this is.
            var worthCompressing = true;

            // One chunk of lookahead: a chunk can only be flagged final once we know nothing follows.
            var current = await ReadChunkAsync(content, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var next = await ReadChunkAsync(content, cancellationToken).ConfigureAwait(false);
                var flags = next.Length == 0 ? ChunkFlags.Final : ChunkFlags.None;

                // The hash and the size are the plaintext's, always, whatever is stored underneath.
                // That is what keeps a blob's identity independent of the compressor: change the
                // deflate implementation between two .NET versions and the same document still
                // deduplicates against the copy stored last year.
                hasher.AppendData(current);
                size += current.Length;

                var payload = current;

                if (worthCompressing && Compress(current) is { } deflated)
                {
                    payload = deflated;
                    flags |= ChunkFlags.Deflated;
                }
                else
                {
                    worthCompressing = false;
                }

                BlobFormat.WriteNonce(nonce, noncePrefix, chunkIndex);

                var sealedChunk = Aead.SealWithNonce(
                    blobKey, nonce, payload, BlobFormat.AssociatedData(chunkIndex, flags));

                var recordHeader = new byte[BlobFormat.RecordHeaderSize];
                recordHeader[0] = (byte)flags;
                BinaryPrimitives.WriteInt32BigEndian(recordHeader.AsSpan(1), sealedChunk.Length);

                await output.WriteAsync(recordHeader, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(sealedChunk, cancellationToken).ConfigureAwait(false);

                chunkIndex++;
                if (flags.HasFlag(ChunkFlags.Final))
                {
                    break;
                }

                current = next;
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return (hasher.GetHashAndReset(), size);
        }
    }

    /// <summary>
    /// The chunk deflated, or null when compressing it is not worth it.
    ///
    /// <para><b>Attempted rather than decided by extension.</b> A list of « these compress, those do
    /// not » is a list that is wrong for somebody: the same <c>.doc</c> extension covers the old
    /// binary format, which halves, and a renamed PDF, which does not. Deflating a megabyte and
    /// looking at the answer costs a fraction of a second and is never wrong.</para>
    ///
    /// <para>And it is only paid once per file: the caller stops asking after the first chunk that
    /// comes back null. A <c>.docx</c> is a ZIP and a scan is already JPEG inside, so the first
    /// megabyte of one settles the other nine.</para>
    ///
    /// <para>The 3% floor is there because a saving smaller than that is not a saving: it buys back
    /// less than the slack in the file system's own block rounding, and it costs an inflate on every
    /// read for the rest of the blob's life.</para>
    /// </summary>
    private static byte[]? Compress(byte[] plaintext)
    {
        if (plaintext.Length == 0)
        {
            return null;
        }

        using var output = new MemoryStream(plaintext.Length);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(plaintext);
        }

        return output.Length <= plaintext.Length - (plaintext.Length / 32) ? output.ToArray() : null;
    }

    private static async Task<byte[]> ReadChunkAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[BlobFormat.ChunkSize];
        var read = await source.ReadAtLeastAsync(
            buffer, BlobFormat.ChunkSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        return read == BlobFormat.ChunkSize ? buffer : buffer[..read];
    }

    /// <summary>Two levels of fan-out so no directory ends up with tens of thousands of entries.</summary>
    private string PathFor(BlobReference blob)
    {
        var plaintextHash = Convert.FromHexString(blob.Sha256);
        var name = Convert.ToHexString(HMACSHA256.HashData(_dataKey.Span, plaintextHash)).ToLowerInvariant();

        return Path.Combine(_rootDirectory, name[..2], name.Substring(2, 2), name + ".blob");
    }

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
            // A leftover temp file is not worth masking the original failure.
        }
    }
}
