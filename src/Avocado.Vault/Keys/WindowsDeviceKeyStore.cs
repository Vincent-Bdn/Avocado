using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Avocado.Vault.Keys;

/// <summary>
/// DPAPI, scoped to the current Windows user. The protected blob is worthless on another machine or
/// under another account — which is exactly the property we want, and exactly why the recovery file
/// is not optional.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceKeyStore : IDeviceKeyStore
{
    /// <summary>
    /// Extra entropy mixed into DPAPI. Not a secret — it only stops an unrelated application's
    /// protected blob from being decryptable as one of ours, and vice versa.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("avocado-vault-device-kek-v1");

    public bool IsSupported => OperatingSystem.IsWindows();

    public string Description => $"Windows ({Environment.MachineName}\\{Environment.UserName})";

    public byte[] Protect(ReadOnlySpan<byte> secret) =>
        ProtectedData.Protect(secret.ToArray(), Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBlob.ToArray(), Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new VaultUnlockFailedException(
                "This vault's device key belongs to a different Windows account or machine. " +
                "Unlock with the recovery key instead.",
                ex);
        }
    }
}
