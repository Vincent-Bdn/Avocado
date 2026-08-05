using System.Security.Cryptography;

namespace Avocado.Vault.Crypto;

/// <summary>
/// A 256-bit symmetric key. Zeroed on dispose so it does not linger in the managed heap any longer
/// than necessary. This is best-effort: the GC may still have moved a copy, and <c>PRAGMA key</c>
/// unavoidably puts the DEK into a string. It raises the cost of a memory scrape, nothing more.
/// </summary>
public sealed class SecretKey : IDisposable
{
    public const int SizeInBytes = 32;

    private byte[]? _bytes;

    public SecretKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
        {
            throw new ArgumentException($"A key must be exactly {SizeInBytes} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public static SecretKey Generate() => new(RandomNumberGenerator.GetBytes(SizeInBytes));

    public ReadOnlySpan<byte> Span =>
        _bytes ?? throw new ObjectDisposedException(nameof(SecretKey));

    /// <summary>Copy of the key material. The caller owns it and should zero it when done.</summary>
    public byte[] ToArray() => Span.ToArray();

    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }
}
