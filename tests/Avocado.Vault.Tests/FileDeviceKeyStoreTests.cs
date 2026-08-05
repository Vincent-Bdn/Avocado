using Avocado.Vault.Keys;

namespace Avocado.Vault.Tests;

public class FileDeviceKeyStoreTests
{
    [Fact]
    public void RoundtripsASecret()
    {
        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);

        var secret = "the key encryption key"u8.ToArray();

        Assert.Equal(secret, store.Unprotect(store.Protect(secret)));
    }

    [Fact]
    public void ReusesOneMachineKeyAcrossVaults()
    {
        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);

        var first = store.Protect("vault one"u8);
        var second = store.Protect("vault two"u8);

        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.Equal("vault one"u8.ToArray(), store.Unprotect(first));
        Assert.Equal("vault two"u8.ToArray(), store.Unprotect(second));
    }

    [Fact]
    public void SurvivesTheProcessRestarting()
    {
        using var directory = new TempDirectory();

        var sealed_ = new FileDeviceKeyStore(directory.Path).Protect("persisted"u8);

        Assert.Equal("persisted"u8.ToArray(), new FileDeviceKeyStore(directory.Path).Unprotect(sealed_));
    }

    [Fact]
    public void AnotherMachineCannotUnprotect()
    {
        using var machineA = new TempDirectory();
        using var machineB = new TempDirectory();

        var sealed_ = new FileDeviceKeyStore(machineA.Path).Protect("privileged"u8);

        Assert.Throws<VaultUnlockFailedException>(() => new FileDeviceKeyStore(machineB.Path).Unprotect(sealed_));
    }

    [Fact]
    public void ReportsAMissingDeviceKeyAsAnUnlockFailure()
    {
        using var machineA = new TempDirectory();
        using var machineB = new TempDirectory();

        var sealed_ = new FileDeviceKeyStore(machineA.Path).Protect("privileged"u8);

        // Restoring a vault folder onto a machine that never had a device key: must point at the
        // recovery key rather than throwing something opaque.
        var exception = Assert.Throws<VaultUnlockFailedException>(
            () => new FileDeviceKeyStore(Path.Combine(machineB.Path, "never-created")).Unprotect(sealed_));

        Assert.Contains("recovery key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsATamperedBlob()
    {
        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);

        var sealed_ = store.Protect("privileged"u8);
        sealed_[^1] ^= 0xFF;

        Assert.Throws<VaultUnlockFailedException>(() => store.Unprotect(sealed_));
    }

    [Fact]
    public void RejectsAMalformedKeyFile()
    {
        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);
        var sealed_ = store.Protect("privileged"u8);

        File.WriteAllBytes(Path.Combine(directory.Path, "device.key"), [1, 2, 3]);

        Assert.Throws<VaultCorruptedException>(() => store.Unprotect(sealed_));
    }

    [Fact]
    public void DoesNotStoreTheSecretInTheClear()
    {
        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);

        var secret = "SECRET-KEK-MATERIAL"u8.ToArray();
        var sealed_ = store.Protect(secret);

        Assert.DoesNotContain(Convert.ToHexString(secret), Convert.ToHexString(sealed_), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Convert.ToHexString(secret),
            Convert.ToHexString(File.ReadAllBytes(Path.Combine(directory.Path, "device.key"))),
            StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheKeyOutsideTheVaultFolder()
    {
        // The reason a stolen vault copy or a synced backup yields nothing.
        using var configDirectory = new TempDirectory();
        using var vaultDirectory = new TempDirectory();

        using var vault = VaultManager.Create(
            vaultDirectory.Path,
            new FileDeviceKeyStore(configDirectory.Path)).Vault;

        Assert.True(File.Exists(Path.Combine(configDirectory.Path, "device.key")));
        Assert.Empty(Directory.GetFiles(vaultDirectory.Path, "device.key", SearchOption.AllDirectories));
    }

    [Fact]
    public void CopyingTheVaultFolderElsewhereNeedsTheRecoveryKey()
    {
        using var configDirectory = new TempDirectory();
        using var original = new TempDirectory();
        using var stolen = new TempDirectory();

        var creation = VaultManager.Create(original.Path, new FileDeviceKeyStore(configDirectory.Path));
        var recoveryCode = creation.RecoveryCode;
        var id = creation.Vault.Id;
        creation.Vault.Dispose();

        foreach (var file in Directory.GetFiles(original.Path))
        {
            File.Copy(file, Path.Combine(stolen.Path, Path.GetFileName(file)));
        }

        using var thiefsConfig = new TempDirectory();
        Assert.Throws<VaultUnlockFailedException>(
            () => VaultManager.UnlockWithDeviceKey(stolen.Path, new FileDeviceKeyStore(thiefsConfig.Path)));

        // The legitimate owner, with the printed sheet, still gets in.
        using var recovered = VaultManager.UnlockWithRecoveryCode(stolen.Path, recoveryCode);
        Assert.Equal(id, recovered.Id);
    }

    [Fact]
    public void RestrictsTheKeyFileToItsOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;  // NTFS inherits the profile ACL; there is no Unix mode to assert.
        }

        using var directory = new TempDirectory();
        var store = new FileDeviceKeyStore(directory.Path);
        store.Protect("privileged"u8);

        var mode = File.GetUnixFileMode(Path.Combine(directory.Path, "device.key"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void ForCurrentPlatformAlwaysReturnsAUsableStore()
    {
        Assert.True(DeviceKeyStore.ForCurrentPlatform().IsSupported);
    }
}
