using System.Buffers.Binary;
using System.Text;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Blobs;

/// <summary>
/// On-disk layout of an encrypted blob.
/// <code>
/// "AVB1" | salt(16) | noncePrefix(8) | record*
/// record := isFinal(1) | length(4, big-endian) | ciphertext‖tag
/// </code>
/// <para>
/// Chunked rather than one AES-GCM pass so a 50 MB scan never has to be fully resident, in plaintext
/// and ciphertext, at the same time.
/// </para>
/// <para>
/// Each chunk's nonce is <c>noncePrefix ‖ counter</c>, unique by construction, and the counter and the
/// final-chunk flag are both authenticated. Dropping a chunk, reordering two, or truncating the file
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

    public static byte[] AssociatedData(long chunkIndex, bool isFinal)
    {
        var data = new byte[Magic.Length + sizeof(long) + 1];
        Magic.CopyTo(data.AsSpan());
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(Magic.Length), chunkIndex);
        data[^1] = isFinal ? (byte)1 : (byte)0;
        return data;
    }
}
