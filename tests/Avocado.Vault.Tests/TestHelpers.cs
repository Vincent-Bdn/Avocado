using System.Security.Cryptography;
using Avocado.Vault.Keys;

namespace Avocado.Vault.Tests;

/// <summary>A scratch folder that cleans up after itself.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "avocado-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}

/// <summary>
/// Stands in for DPAPI so the tests run identically on Linux and macOS CI. A new instance models a
/// different machine or user account — which is exactly the scenario the recovery key exists for.
/// </summary>
public sealed class FakeDeviceKeyStore : IDeviceKeyStore
{
    private readonly byte[] _machineKey = RandomNumberGenerator.GetBytes(32);

    public FakeDeviceKeyStore(string description = "Fake device") => Description = description;

    public bool IsSupported => true;

    public string Description { get; }

    public byte[] Protect(ReadOnlySpan<byte> secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[secret.Length];
        var tag = new byte[16];

        using var gcm = new AesGcm(_machineKey, 16);
        gcm.Encrypt(nonce, secret, ciphertext, tag);

        return [.. nonce, .. ciphertext, .. tag];
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        try
        {
            var nonce = protectedBlob[..12];
            var ciphertext = protectedBlob[12..^16];
            var tag = protectedBlob[^16..];

            var plaintext = new byte[ciphertext.Length];
            using var gcm = new AesGcm(_machineKey, 16);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new VaultUnlockFailedException("Different fake machine.", ex);
        }
    }
}
