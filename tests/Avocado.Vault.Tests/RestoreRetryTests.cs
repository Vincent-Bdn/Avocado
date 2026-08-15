using Avocado.Vault.Backups;

namespace Avocado.Vault.Tests;

public class RestoreRetryTests
{
    /// <summary>
    /// Fifty-four characters off a printed sheet, on the day the laptop was stolen. One typo has to
    /// cost nothing but retyping.
    ///
    /// <para>It used to cost the folder: the keyring was written into the destination and the key
    /// checked afterwards, so a refused attempt left a vault.json behind, and that file is precisely
    /// what "a vault already exists here" tests for. The second attempt, with the right key, was
    /// turned away.</para>
    /// </summary>
    [Fact]
    public async Task AMistypedRecoveryKeyLeavesTheDestinationUsable()
    {
        using var original = new TempDirectory();
        using var destination = new TempDirectory();
        using var replacement = new TempDirectory();

        var creation = VaultManager.Create(original.Path, new FakeDeviceKeyStore());
        using (var vault = creation.Vault)
        {
            await new BackupMirror(vault, Sink(destination))
                .PushAsync(vault.CreateBackup("auto"), SnapshotRetention.Default);
        }

        var candidate = (await VaultRestore.DiscoverAsync(Sink(destination)))[0];
        var target = Path.Combine(replacement.Path, "coffre");

        await Assert.ThrowsAsync<VaultUnlockFailedException>(() => VaultRestore.RestoreAsync(
            Sink(destination), candidate.VaultId, candidate.Snapshots[0].Path,
            target, "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FFFFFF-GGGGGG-HHHHHH-JJJJJJ",
            new FakeDeviceKeyStore()));

        // Nothing of a vault left behind, so the second attempt is not refused for the wrong reason.
        Assert.False(File.Exists(Path.Combine(target, "vault.json")));

        using var restored = await VaultRestore.RestoreAsync(
            Sink(destination), candidate.VaultId, candidate.Snapshots[0].Path,
            target, creation.RecoveryCode, new FakeDeviceKeyStore());

        Assert.Equal(candidate.VaultId, restored.Id);
    }

    private static IBackupSink Sink(TempDirectory directory) =>
        new DirectorySink(new FixedPathLocator(directory.Path, "Test"));
}
