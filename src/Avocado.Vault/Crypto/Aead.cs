using System.Security.Cryptography;

namespace Avocado.Vault.Crypto;

/// <summary>
/// AES-256-GCM, the only authenticated cipher used by the vault.
/// Sealed form is <c>nonce(12) || ciphertext || tag(16)</c>.
/// </summary>
public static class Aead
{
    public const int NonceSize = 12;
    public const int TagSize = 16;

    /// <summary>Overhead a sealed buffer adds over its plaintext.</summary>
    public const int Overhead = NonceSize + TagSize;

    public static byte[] Seal(SecretKey key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        var ciphertext = output.AsSpan(NonceSize, plaintext.Length);
        var tag = output.AsSpan(NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var gcm = new AesGcm(key.Span, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return output;
    }

    /// <summary>
    /// Seals with a caller-supplied nonce. Only for the chunked blob format, where nonces are derived
    /// from a random per-blob prefix plus a chunk counter. Reusing a (key, nonce) pair with GCM is
    /// catastrophic — never call this unless uniqueness is structurally guaranteed.
    /// </summary>
    public static byte[] SealWithNonce(
        SecretKey key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException($"A nonce must be exactly {NonceSize} bytes.", nameof(nonce));
        }

        var output = new byte[plaintext.Length + TagSize];
        using var gcm = new AesGcm(key.Span, TagSize);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associatedData);
        return output;
    }

    /// <exception cref="CryptographicException">The key is wrong or the data was tampered with.</exception>
    public static byte[] Open(SecretKey key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        if (sealedData.Length < Overhead)
        {
            throw new CryptographicException("Sealed data is too short to be valid.");
        }

        var nonce = sealedData[..NonceSize];
        var ciphertext = sealedData[NonceSize..^TagSize];
        var tag = sealedData[^TagSize..];

        var plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(key.Span, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    /// <summary>Opens a buffer sealed by <see cref="SealWithNonce"/>, into a caller-supplied span.</summary>
    public static void OpenWithNonce(
        SecretKey key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> sealedData,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        if (sealedData.Length < TagSize)
        {
            throw new CryptographicException("Sealed chunk is too short to be valid.");
        }

        var ciphertext = sealedData[..^TagSize];
        var tag = sealedData[^TagSize..];

        using var gcm = new AesGcm(key.Span, TagSize);
        gcm.Decrypt(nonce, ciphertext, tag, destination[..ciphertext.Length], associatedData);
    }
}
