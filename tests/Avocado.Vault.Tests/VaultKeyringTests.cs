using System.Text.Json;
using Avocado.Vault.Crypto;
using Avocado.Vault.Keys;

namespace Avocado.Vault.Tests;

public class VaultKeyringTests
{
    private static readonly Argon2Parameters CheapKdf =
        new() { MemoryKib = 1024, Iterations = 1, Parallelism = 1 };

    [Fact]
    public void CreateEnrolsBothADeviceKeyAndARecoveryKey()
    {
        using var directory = new TempDirectory();

        var creation = VaultKeyring.Create(directory.Combine("vault.json"), new FakeDeviceKeyStore());
        using var dataKey = creation.DataKey;

        Assert.True(creation.Keyring.HasDeviceKey);
        Assert.True(creation.Keyring.HasRecoveryKey);
        Assert.False(creation.Keyring.HasPassphrase);
        Assert.NotEmpty(creation.RecoveryCode);
    }

    [Fact]
    public void CreateStillWorksWithoutAnOsKeyStore()
    {
        using var directory = new TempDirectory();

        // macOS and Linux until their key stores are wired up.
        var creation = VaultKeyring.Create(directory.Combine("vault.json"), new UnsupportedDeviceKeyStore());
        using var dataKey = creation.DataKey;

        Assert.False(creation.Keyring.HasDeviceKey);
        Assert.True(creation.Keyring.HasRecoveryKey);
    }

    [Fact]
    public void EveryUnlockPathYieldsTheSameDataKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");
        var deviceKeyStore = new FakeDeviceKeyStore();

        var creation = VaultKeyring.Create(path, deviceKeyStore);
        using var original = creation.DataKey;
        creation.Keyring.SetPassphrase(original, "un mot de passe", CheapKdf);

        var reloaded = VaultKeyring.Load(path);
        using var viaDevice = reloaded.UnlockWithDeviceKey(deviceKeyStore);
        using var viaRecovery = reloaded.UnlockWithRecoveryCode(creation.RecoveryCode);
        using var viaPassphrase = reloaded.UnlockWithPassphrase("un mot de passe");

        Assert.True(original.Span.SequenceEqual(viaDevice.Span));
        Assert.True(original.Span.SequenceEqual(viaRecovery.Span));
        Assert.True(original.Span.SequenceEqual(viaPassphrase.Span));
    }

    [Fact]
    public void ADeviceKeyFromAnotherMachineDoesNotOpenTheVault()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore("Laptop"));
        creation.DataKey.Dispose();

        var reloaded = VaultKeyring.Load(path);

        // The whole reason the recovery key is not optional.
        Assert.Throws<VaultUnlockFailedException>(
            () => reloaded.UnlockWithDeviceKey(new FakeDeviceKeyStore("Replacement laptop")));
    }

    [Fact]
    public void RegeneratingTheRecoveryKeyInvalidatesThePreviousOne()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        using var dataKey = creation.DataKey;

        var replacement = creation.Keyring.RegenerateRecoveryKey(dataKey);

        var reloaded = VaultKeyring.Load(path);
        using var viaNew = reloaded.UnlockWithRecoveryCode(replacement);
        Assert.True(dataKey.Span.SequenceEqual(viaNew.Span));

        Assert.Throws<VaultUnlockFailedException>(() => reloaded.UnlockWithRecoveryCode(creation.RecoveryCode));
    }

    [Fact]
    public void ChangingThePassphraseDoesNotChangeTheDataKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        using var dataKey = creation.DataKey;

        creation.Keyring.SetPassphrase(dataKey, "first", CheapKdf);
        creation.Keyring.SetPassphrase(dataKey, "second", CheapKdf);

        var reloaded = VaultKeyring.Load(path);
        using var unlocked = reloaded.UnlockWithPassphrase("second");

        // The point of the envelope: re-wrapping is O(1) and never touches vault data.
        Assert.True(dataKey.Span.SequenceEqual(unlocked.Span));
        Assert.Throws<VaultUnlockFailedException>(() => reloaded.UnlockWithPassphrase("first"));
    }

    [Fact]
    public void ARejectedPassphraseSaysSoRatherThanThrowingCrypto()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        using var dataKey = creation.DataKey;
        creation.Keyring.SetPassphrase(dataKey, "right", CheapKdf);

        var reloaded = VaultKeyring.Load(path);

        Assert.Throws<VaultUnlockFailedException>(() => reloaded.UnlockWithPassphrase("wrong"));
    }

    [Fact]
    public void RefusesToRemoveTheLastRemainingUnlockPath()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new UnsupportedDeviceKeyStore());
        creation.DataKey.Dispose();

        var only = Assert.Single(creation.Keyring.Keys);

        var exception = Assert.Throws<VaultException>(() => creation.Keyring.Remove(only.Id));
        Assert.Contains("only way left", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovesARevokedDeviceKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore("Old laptop"));
        creation.DataKey.Dispose();

        var device = creation.Keyring.Keys.Single(k => k.Kind == VaultKeyKind.Device);
        creation.Keyring.Remove(device.Id);

        var reloaded = VaultKeyring.Load(path);
        Assert.False(reloaded.HasDeviceKey);
        Assert.True(reloaded.HasRecoveryKey);
    }

    [Fact]
    public void ReEnrollingOnTheSameMachineReplacesRatherThanAccumulates()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");
        var deviceKeyStore = new FakeDeviceKeyStore("Same laptop");

        var creation = VaultKeyring.Create(path, deviceKeyStore);
        using var dataKey = creation.DataKey;

        creation.Keyring.EnrollDeviceKey(dataKey, deviceKeyStore);
        creation.Keyring.EnrollDeviceKey(dataKey, deviceKeyStore);

        Assert.Single(creation.Keyring.Keys, k => k.Kind == VaultKeyKind.Device);
    }

    [Fact]
    public void SupportsTheSameVaultOnTwoMachines()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");
        var laptop = new FakeDeviceKeyStore("Laptop");
        var desktop = new FakeDeviceKeyStore("Office desktop");

        var creation = VaultKeyring.Create(path, laptop);
        using var dataKey = creation.DataKey;
        creation.Keyring.EnrollDeviceKey(dataKey, desktop);

        var reloaded = VaultKeyring.Load(path);
        using var viaLaptop = reloaded.UnlockWithDeviceKey(laptop);
        using var viaDesktop = reloaded.UnlockWithDeviceKey(desktop);

        Assert.True(dataKey.Span.SequenceEqual(viaLaptop.Span));
        Assert.True(dataKey.Span.SequenceEqual(viaDesktop.Span));
    }

    [Fact]
    public void AWrappedKeyCannotBeTransplantedIntoAnotherVault()
    {
        using var directory = new TempDirectory();
        var victimPath = directory.Combine("victim.json");
        var attackerPath = directory.Combine("attacker.json");

        var attackerStore = new FakeDeviceKeyStore("Attacker");
        var victim = VaultKeyring.Create(victimPath, new FakeDeviceKeyStore("Victim"));
        var attacker = VaultKeyring.Create(attackerPath, attackerStore);
        victim.DataKey.Dispose();
        attacker.DataKey.Dispose();

        // Splice the attacker's device entry into the victim's keyring.
        var victimDocument = ReadDocument(victimPath);
        var attackerDocument = ReadDocument(attackerPath);
        var spliced = victimDocument with { Keys = [.. victimDocument.Keys, .. attackerDocument.Keys] };
        WriteDocument(victimPath, spliced);

        // The associated data binds each wrapping to its vault id, so the graft is inert.
        Assert.Throws<VaultUnlockFailedException>(
            () => VaultKeyring.Load(victimPath).UnlockWithDeviceKey(attackerStore));
    }

    [Fact]
    public void RejectsATamperedWrappedKey()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");
        var deviceKeyStore = new FakeDeviceKeyStore();

        var creation = VaultKeyring.Create(path, deviceKeyStore);
        creation.DataKey.Dispose();

        var document = ReadDocument(path);
        var entry = document.Keys[0];
        entry.WrappedDataKey[^1] ^= 0xFF;
        WriteDocument(path, document);

        Assert.Throws<VaultUnlockFailedException>(
            () => VaultKeyring.Load(path).UnlockWithDeviceKey(deviceKeyStore));
    }

    [Fact]
    public void RefusesAKeyringFromANewerVersion()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        creation.DataKey.Dispose();

        WriteDocument(path, ReadDocument(path) with { Version = 99 });

        var exception = Assert.Throws<VaultCorruptedException>(() => VaultKeyring.Load(path));
        Assert.Contains("newer version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnEmptyKeyring()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        creation.DataKey.Dispose();

        WriteDocument(path, ReadDocument(path) with { Keys = [] });

        Assert.Throws<VaultCorruptedException>(() => VaultKeyring.Load(path));
    }

    [Fact]
    public void ReportsAMissingKeyringClearly()
    {
        using var directory = new TempDirectory();

        Assert.Throws<VaultCorruptedException>(() => VaultKeyring.Load(directory.Combine("absent.json")));
    }

    [Fact]
    public void RefusesToOverwriteAnExistingKeyring()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        VaultKeyring.Create(path, new FakeDeviceKeyStore()).DataKey.Dispose();

        Assert.Throws<VaultException>(() => VaultKeyring.Create(path, new FakeDeviceKeyStore()));
    }

    [Fact]
    public void DoesNotWriteTheDataKeyToDisk()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        var creation = VaultKeyring.Create(path, new FakeDeviceKeyStore());
        using var dataKey = creation.DataKey;

        var onDisk = File.ReadAllText(path);

        Assert.DoesNotContain(Convert.ToHexString(dataKey.Span), onDisk, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(dataKey.Span), onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesNoTemporaryFileBehind()
    {
        using var directory = new TempDirectory();
        var path = directory.Combine("vault.json");

        VaultKeyring.Create(path, new FakeDeviceKeyStore()).DataKey.Dispose();

        Assert.False(File.Exists(path + ".tmp"));
    }

    private static VaultKeyringDocument ReadDocument(string path) =>
        JsonSerializer.Deserialize<VaultKeyringDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
        ?? throw new InvalidOperationException("Unreadable keyring.");

    private static void WriteDocument(string path, VaultKeyringDocument document) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                document,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
}
