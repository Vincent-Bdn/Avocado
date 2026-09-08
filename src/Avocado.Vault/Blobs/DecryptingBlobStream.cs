using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Blobs;

/// <summary>Forward-only reader over the chunked blob format.</summary>
internal sealed class DecryptingBlobStream : Stream
{
    private readonly Stream _source;
    private readonly SecretKey _blobKey;
    private readonly byte[] _noncePrefix;

    private byte[] _plaintext = [];
    private int _plaintextOffset;
    private long _chunkIndex;
    private bool _sawFinalChunk;

    private DecryptingBlobStream(Stream source, SecretKey blobKey, byte[] noncePrefix)
    {
        _source = source;
        _blobKey = blobKey;
        _noncePrefix = noncePrefix;
    }

    public static DecryptingBlobStream Open(Stream source, SecretKey dataKey)
    {
        try
        {
            Span<byte> header = stackalloc byte[BlobFormat.HeaderSize];
            source.ReadExactly(header);

            if (!header[..BlobFormat.Magic.Length].SequenceEqual(BlobFormat.Magic))
            {
                throw new VaultCorruptedException("This file is not an Avocado blob.");
            }

            var salt = header.Slice(BlobFormat.Magic.Length, BlobFormat.SaltSize);
            var noncePrefix = header.Slice(BlobFormat.Magic.Length + BlobFormat.SaltSize, BlobFormat.NoncePrefixSize);

            return new DecryptingBlobStream(source, BlobFormat.DeriveBlobKey(dataKey, salt), noncePrefix.ToArray());
        }
        catch (EndOfStreamException ex)
        {
            source.Dispose();
            throw new VaultCorruptedException("This blob is truncated: it has no readable header.", ex);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_plaintextOffset >= _plaintext.Length)
        {
            if (_sawFinalChunk || !TryReadChunk())
            {
                return 0;
            }
        }

        var available = Math.Min(buffer.Length, _plaintext.Length - _plaintextOffset);
        _plaintext.AsSpan(_plaintextOffset, available).CopyTo(buffer);
        _plaintextOffset += available;
        return available;
    }

    private bool TryReadChunk()
    {
        Span<byte> recordHeader = stackalloc byte[BlobFormat.RecordHeaderSize];
        try
        {
            _source.ReadExactly(recordHeader);
        }
        catch (EndOfStreamException ex)
        {
            // The final chunk is flagged, so running out of records before seeing it means the file
            // was cut short.
            throw new VaultCorruptedException("This blob is truncated: it ends before its final chunk.", ex);
        }

        var flags = (ChunkFlags)recordHeader[0];
        var isFinal = flags.HasFlag(ChunkFlags.Final);
        var sealedLength = BinaryPrimitives.ReadInt32BigEndian(recordHeader[1..]);

        if (sealedLength < Aead.TagSize || sealedLength > BlobFormat.ChunkSize + Aead.TagSize)
        {
            throw new VaultCorruptedException("This blob declares an implausible chunk length.");
        }

        var sealedChunk = new byte[sealedLength];
        try
        {
            _source.ReadExactly(sealedChunk);
        }
        catch (EndOfStreamException ex)
        {
            throw new VaultCorruptedException("This blob is truncated mid-chunk.", ex);
        }

        Span<byte> nonce = stackalloc byte[Aead.NonceSize];
        BlobFormat.WriteNonce(nonce, _noncePrefix, _chunkIndex);

        var plaintext = new byte[sealedLength - Aead.TagSize];
        try
        {
            Aead.OpenWithNonce(
                _blobKey,
                nonce,
                sealedChunk,
                BlobFormat.AssociatedData(_chunkIndex, flags),
                plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new VaultCorruptedException(
                "This blob failed authentication, it was modified, reordered or truncated since it was written.",
                ex);
        }

        // Only after the tag has verified, never before: inflating is the one operation here that can
        // be made to consume far more than it is given, and the authentication is what guarantees
        // these bytes are the ones this vault wrote.
        if (flags.HasFlag(ChunkFlags.Deflated))
        {
            plaintext = Inflate(plaintext);
        }

        _plaintext = plaintext;
        _plaintextOffset = 0;
        _chunkIndex++;
        _sawFinalChunk = isFinal;

        return plaintext.Length > 0 || !isFinal;
    }

    /// <summary>
    /// Undoes <c>EncryptedBlobStore.Compress</c>. Bounded by the chunk size the writer worked in,
    /// which no honest chunk can exceed, so a blob that inflates past it is corrupt whatever its tag
    /// said.
    /// </summary>
    private static byte[] Inflate(byte[] deflated)
    {
        using var source = new MemoryStream(deflated);
        using var inflater = new DeflateStream(source, CompressionMode.Decompress);
        using var plaintext = new MemoryStream(deflated.Length * 2);

        var buffer = new byte[64 * 1024];
        int read;

        try
        {
            while ((read = inflater.Read(buffer)) > 0)
            {
                if (plaintext.Length + read > BlobFormat.ChunkSize)
                {
                    throw new VaultCorruptedException("This blob declares a chunk that inflates past its own limit.");
                }

                plaintext.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new VaultCorruptedException("This blob holds a compressed chunk that cannot be read back.", ex);
        }

        return plaintext.ToArray();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blobKey.Dispose();
            _source.Dispose();
        }

        base.Dispose(disposing);
    }
}
