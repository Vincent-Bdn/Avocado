using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Avocado.Vault.Crypto;

/// <summary>Argon2id cost parameters, stored per key entry so they can be raised later without
/// invalidating vaults created under the old settings.</summary>
public sealed record Argon2Parameters
{
    /// <summary>
    /// 64 MiB / t=3 / p=4. Comfortably above the OWASP floor, and Konscious is a pure-C#
    /// implementation — pushing memory much higher makes unlock take seconds on a modest laptop.
    /// </summary>
    public static Argon2Parameters Default { get; } = new();

    public int MemoryKib { get; init; } = 64 * 1024;
    public int Iterations { get; init; } = 3;
    public int Parallelism { get; init; } = 4;
}

public static class KeyDerivation
{
    /// <summary>
    /// Derives a subkey from full-entropy input material. Cheap by design — only valid when the input
    /// is already a random 256-bit secret (a DEK, a recovery key). Never for a passphrase.
    /// </summary>
    public static SecretKey Hkdf(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt, string info)
    {
        Span<byte> output = stackalloc byte[SecretKey.SizeInBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, inputKeyMaterial, output, salt, Encoding.UTF8.GetBytes(info));
        var key = new SecretKey(output);
        CryptographicOperations.ZeroMemory(output);
        return key;
    }

    /// <summary>Stretches a human passphrase into a key encryption key.</summary>
    public static SecretKey Argon2id(string passphrase, ReadOnlySpan<byte> salt, Argon2Parameters parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        if (salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            using var argon2 = new Argon2id(passphraseBytes)
            {
                Salt = salt.ToArray(),
                MemorySize = parameters.MemoryKib,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };

            var derived = argon2.GetBytes(SecretKey.SizeInBytes);
            try
            {
                return new SecretKey(derived);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }
}
