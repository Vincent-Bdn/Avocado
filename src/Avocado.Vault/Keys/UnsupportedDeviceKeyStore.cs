namespace Avocado.Vault.Keys;

/// <summary>
/// Placeholder for platforms whose OS key store is not wired up yet.
/// <para>
/// TODO: macOS via Security.framework (SecItemAdd / SecItemCopyMatching) and Linux via libsecret.
/// Both are straightforward P/Invoke, but they cannot be verified from a Windows dev box, so they are
/// deliberately absent rather than written blind. Until then, macOS and Linux users unlock with a
/// passphrase or the recovery key — the vault itself is fully cross-platform, only the
/// open-without-typing-anything convenience is Windows-only.
/// </para>
/// </summary>
public sealed class UnsupportedDeviceKeyStore : IDeviceKeyStore
{
    public bool IsSupported => false;

    public string Description => $"Unavailable on {Environment.OSVersion.Platform}";

    public byte[] Protect(ReadOnlySpan<byte> secret) => throw Unavailable();

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob) => throw Unavailable();

    private static DeviceKeyStoreUnavailableException Unavailable() =>
        new("No OS-backed key store is available on this platform. " +
            "Unlock with a passphrase or the recovery key.");
}
