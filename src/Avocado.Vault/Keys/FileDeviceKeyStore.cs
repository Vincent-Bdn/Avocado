using System.Security.Cryptography;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Keys;

/// <summary>
/// Device key store for macOS and Linux: a random machine key held outside the vault folder, in the
/// user's config directory with <c>0600</c> permissions, used to seal each vault's KEK.
/// <para>
/// <b>Why not the Keychain / libsecret.</b> The proper macOS answer is Security.framework
/// (<c>SecItemAdd</c> / <c>SecItemCopyMatching</c>), which adds per-application ACLs on top of the
/// login password. That is ~250 lines of CoreFoundation interop guarding the key to every document in
/// the practice, and it cannot be exercised from the machine this was written on — a bug there is
/// either silent data loss or a silent hole. This store is small enough to be read in one sitting and
/// is covered by tests on all three platforms. See TODO.md; the interface is unchanged when the
/// Keychain version lands, and existing vaults keep working through their recovery key.
/// </para>
/// <para>
/// <b>What it protects.</b> Same as DPAPI for the threats that matter here: the key is not in the
/// vault folder, so a stolen vault copy, a stolen backup, or a synced folder yields nothing. What
/// Keychain would add is protection from another process running as the same user — which the stated
/// threat model already excludes. On a machine with FileVault or LUKS enabled, a stolen disk is
/// covered too; without full-disk encryption, this key is readable from the raw disk, and so, for
/// practical purposes, is a DPAPI master key.
/// </para>
/// </summary>
public sealed class FileDeviceKeyStore : IDeviceKeyStore
{
    private const string KeyFileName = "device.key";

    private readonly string _keyFilePath;

    public FileDeviceKeyStore(string? configDirectory = null)
    {
        var directory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create),
            "Avocado");

        _keyFilePath = Path.Combine(directory, KeyFileName);
    }

    public bool IsSupported => true;

    public string Description => $"{PlatformName()} ({Environment.MachineName}/{Environment.UserName})";

    public byte[] Protect(ReadOnlySpan<byte> secret)
    {
        using var machineKey = LoadOrCreateMachineKey();
        return Aead.Seal(machineKey, secret, AssociatedData);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        if (!File.Exists(_keyFilePath))
        {
            throw new VaultUnlockFailedException(
                $"No device key at '{_keyFilePath}'. This vault was enrolled on a different machine or " +
                "user account. Unlock with the recovery key instead.");
        }

        using var machineKey = LoadOrCreateMachineKey();
        try
        {
            return Aead.Open(machineKey, protectedBlob, AssociatedData);
        }
        catch (CryptographicException ex)
        {
            throw new VaultUnlockFailedException(
                "This machine's device key does not open the vault. Use the recovery key instead.", ex);
        }
    }

    private static ReadOnlySpan<byte> AssociatedData => "avocado-vault-device-kek-v1"u8;

    private SecretKey LoadOrCreateMachineKey()
    {
        if (File.Exists(_keyFilePath))
        {
            var existing = File.ReadAllBytes(_keyFilePath);
            try
            {
                if (existing.Length != SecretKey.SizeInBytes)
                {
                    throw new VaultCorruptedException(
                        $"The device key at '{_keyFilePath}' is malformed. Delete it and unlock with the " +
                        "recovery key to re-enrol this machine.");
                }

                return new SecretKey(existing);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(existing);
            }
        }

        var directory = Path.GetDirectoryName(_keyFilePath)!;
        Directory.CreateDirectory(directory);
        RestrictToOwner(directory, isDirectory: true);

        var machineKey = SecretKey.Generate();
        var bytes = machineKey.ToArray();
        try
        {
            // Create the file with owner-only permissions before any bytes land in it, rather than
            // writing world-readable and tightening afterwards.
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(_keyFilePath, options))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            return machineKey;
        }
        catch
        {
            machineKey.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void RestrictToOwner(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            // NTFS inherits the user profile's ACL, which is already owner-only.
            return;
        }

        var mode = isDirectory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;

        File.SetUnixFileMode(path, mode);
    }

    private static string PlatformName() =>
        OperatingSystem.IsMacOS() ? "macOS"
        : OperatingSystem.IsLinux() ? "Linux"
        : Environment.OSVersion.Platform.ToString();
}
