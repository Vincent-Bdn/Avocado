using System.Buffers.Binary;
using System.Text;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Blobs;

/// <summary>
/// On-disk layout of an encrypted blob.
/// <code>
/// "AVB1" | salt(16) | noncePrefix(8) | record*
/// record := flags(1) | length(4, big-endian) | ciphertext‖tag
/// </code>
/// <para>
/// Chunked rather than one AES-GCM pass so a 50 MB scan never has to be fully resident, in plaintext
/// and ciphertext, at the same time.
/// </para>
/// <para>
/// Each chunk's nonce is <c>noncePrefix ‖ counter</c>, unique by construction, and the counter and the
/// chunk's flags are both authenticated. Dropping a chunk, reordering two, or truncating the file
/// early therefore fails to decrypt, a plain per-chunk AEAD without that binding would not notice.
/// </para>
/// </summary>
internal static class BlobFormat
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("AVB1");

    public const int SaltSize = 16;
    public const int NoncePrefixSize = 8;
    public const int HeaderSize = 4 + SaltSize + NoncePrefixSize;
    public const int RecordHeaderSize = 1 + 4;
    public const int ChunkSize = 1024 * 1024;

    public const string BlobKeyInfo = "avocado-blob-v1";

    public static SecretKey DeriveBlobKey(SecretKey dataKey, ReadOnlySpan<byte> salt) =>
        KeyDerivation.Hkdf(dataKey.Span, salt, BlobKeyInfo);

    public static void WriteNonce(Span<byte> destination, ReadOnlySpan<byte> noncePrefix, long chunkIndex)
    {
        noncePrefix.CopyTo(destination);
        BinaryPrimitives.WriteUInt32BigEndian(destination[NoncePrefixSize..], checked((uint)chunkIndex));
    }

    public static byte[] AssociatedData(long chunkIndex, ChunkFlags flags)
    {
        var data = new byte[Magic.Length + sizeof(long) + 1];
        Magic.CopyTo(data.AsSpan());
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(Magic.Length), chunkIndex);
        data[^1] = (byte)flags;
        return data;
    }
}

/// <summary>
/// The record header's first byte, and part of what each chunk authenticates.
///
/// <para><b>Why this is not a new format version.</b> The byte used to hold nothing but « is this the
/// last chunk », written as 0 or 1, and the same byte went into the associated data. <see
/// cref="Final"/> is that 1. So every blob written before compression existed decodes to exactly the
/// flags it was written with, authenticates against exactly the associated data it was sealed with,
/// and reads today without a compatibility branch anywhere.</para>
///
/// <para>And because the flags are authenticated rather than merely stored, nobody can flip <see
/// cref="Deflated"/> on a chunk that is not: the tag stops verifying. A compression bit that only
/// lived in the clear part of the header would turn tampering into a decompression bomb.</para>
/// </summary>
[Flags]
internal enum ChunkFlags : byte
{
    None = 0,

    /// <summary>Nothing follows. Authenticated, so a truncation cannot pass for an ending.</summary>
    Final = 1,

    /// <summary>The ciphertext holds a raw-deflate stream rather than the bytes themselves.</summary>
    Deflated = 2,
}
