namespace Avocado.Vault.Keys;

/// <summary>
/// Wraps a secret with a key held by the operating system and bound to the current user account, so
/// the vault opens on a double-click without a passphrase.
/// <para>
/// This is what makes the app usable daily. It protects against a stolen drive, a stolen backup, or a
/// stolen NAS, not against anyone already logged into the user's session. That session already has a
/// password; see the threat model in TODO.md.
/// </para>
/// </summary>
public interface IDeviceKeyStore
{
    bool IsSupported { get; }

    /// <summary>Human-readable description of where the key lives, shown in the keyring UI.</summary>
    string Description { get; }

    byte[] Protect(ReadOnlySpan<byte> secret);

    /// <exception cref="VaultUnlockFailedException">
    /// The blob was produced by another user account or another machine.
    /// </exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedBlob);
}

public static class DeviceKeyStore
{
    /// <summary>
    /// The device key store for the current platform: DPAPI on Windows, an owner-only machine key
    /// file elsewhere. Every supported platform opens without a passphrase.
    /// </summary>
    public static IDeviceKeyStore ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsDeviceKeyStore()
        : OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ? new FileDeviceKeyStore()
        : new UnsupportedDeviceKeyStore();
}
