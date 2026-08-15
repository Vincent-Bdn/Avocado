using System.Text;
using Avocado.Vault.Backups;
using Avocado.Vault.Blobs;

namespace Avocado.Vault.Tests;

public class BackupTests
{
    /// <summary>
    /// The one that matters: the laptop is gone, and everything is rebuilt somewhere else from a
    /// destination and the recovery key. Records and documents, not one or the other.
    /// </summary>
    [Fact]
    public async Task RebuildsAWholePracticeOnAReplacementMachine()
    {
        using var original = new TempDirectory();
        using var destination = new TempDirectory();
        using var replacement = new TempDirectory();

        var creation = VaultManager.Create(original.Path, new FakeDeviceKeyStore("Drowned laptop"));
        Guid vaultId;
        BlobReference scan;

        using (var vault = creation.Vault)
        {
            scan = await vault.Blobs.PutAsync(new MemoryStream("Assignation, 14 pages."u8.ToArray()));
            vaultId = vault.Id;

            Execute(vault, "CREATE TABLE matters (reference TEXT);");
            Execute(vault, "INSERT INTO matters VALUES ('2026-014 Dupont c/ Martin');");

            var snapshot = vault.CreateBackup("auto");
            var outcome = await new BackupMirror(vault, Sink(destination)).PushAsync(snapshot, SnapshotRetention.Default);

            Assert.False(outcome.Skipped);
            Assert.Equal(1, outcome.BlobsUploaded);
            Assert.Equal(0, outcome.SnapshotsPruned);
        }

        var candidates = await VaultRestore.DiscoverAsync(Sink(destination));
        var candidate = Assert.Single(candidates);
        Assert.Equal(vaultId, candidate.VaultId);
        Assert.Equal(1, candidate.BlobCount);

        using var restored = await VaultRestore.RestoreAsync(
            Sink(destination),
            candidate.VaultId,
            candidate.Snapshots[0].Path,
            replacement.Path,
            creation.RecoveryCode,
            new FakeDeviceKeyStore("Replacement laptop"));

        Assert.Equal(vaultId, restored.Id);

        using (var reader = new StreamReader(restored.Blobs.OpenRead(scan)))
        {
            Assert.Equal("Assignation, 14 pages.", await reader.ReadToEndAsync());
        }

        Assert.Equal("2026-014 Dupont c/ Martin", Scalar(restored, "SELECT reference FROM matters;"));
    }

    /// <summary>
    /// A restored vault has to open on its own the next morning, otherwise the recovery key becomes
    /// something typed every day and then written on a sticky note.
    /// </summary>
    [Fact]
    public async Task TheRestoredVaultOpensWithTheNewMachinesOwnDeviceKey()
    {
        using var original = new TempDirectory();
        using var destination = new TempDirectory();
        using var replacement = new TempDirectory();

        var creation = VaultManager.Create(original.Path, new FakeDeviceKeyStore("Old"));
        using (var vault = creation.Vault)
        {
            await new BackupMirror(vault, Sink(destination)).PushAsync(vault.CreateBackup("auto"), SnapshotRetention.Default);
        }

        var newMachine = new FakeDeviceKeyStore("New");
        var candidate = (await VaultRestore.DiscoverAsync(Sink(destination)))[0];

        using (await VaultRestore.RestoreAsync(
                   Sink(destination), candidate.VaultId, candidate.Snapshots[0].Path,
                   replacement.Path, creation.RecoveryCode, newMachine))
        {
        }

        using var reopened = VaultManager.UnlockWithDeviceKey(replacement.Path, newMachine);
        Assert.Equal(candidate.VaultId, reopened.Id);
    }

    /// <summary>
    /// Blobs are content-addressed and immutable, so the second backup of an unchanged practice should
    /// move no documents at all. This is the whole reason backing up gigabytes of scans to a USB key
    /// is practical rather than theoretical.
    /// </summary>
    [Fact]
    public async Task SendsEachDocumentOnceAndOnlyOnce()
    {
        using var original = new TempDirectory();
        using var destination = new TempDirectory();

        using var vault = VaultManager.Create(original.Path, new FakeDeviceKeyStore()).Vault;
        await vault.Blobs.PutAsync(new MemoryStream("Conclusions"u8.ToArray()));

        var first = await new BackupMirror(vault, Sink(destination))
            .PushAsync(vault.CreateBackup("first"), SnapshotRetention.Default);
        var second = await new BackupMirror(vault, Sink(destination))
            .PushAsync(vault.CreateBackup("second"), SnapshotRetention.Default);

        Assert.Equal(1, first.BlobsUploaded);
        Assert.Equal(0, second.BlobsUploaded);
        Assert.Equal(0, second.BytesUploaded);

        await vault.Blobs.PutAsync(new MemoryStream("Pièce n°3"u8.ToArray()));
        var third = await new BackupMirror(vault, Sink(destination))
            .PushAsync(vault.CreateBackup("third"), SnapshotRetention.Default);

        Assert.Equal(1, third.BlobsUploaded);
    }

    /// <summary>
    /// A destination that is not plugged in is the normal state of a USB key, not an error, and must
    /// never be reported as one.
    /// </summary>
    [Fact]
    public async Task AnUnpluggedDestinationIsSkippedRatherThanFailed()
    {
        using var original = new TempDirectory();
        using var vault = VaultManager.Create(original.Path, new FakeDeviceKeyStore()).Vault;

        var absent = new DirectorySink(new MarkedVolumeLocator(Guid.NewGuid(), "Clé absente"));
        var outcome = await new BackupMirror(vault, absent).PushAsync(vault.CreateBackup("auto"), SnapshotRetention.Default);

        Assert.True(outcome.Skipped);
        Assert.Equal(0, outcome.BlobsUploaded);
    }

    /// <summary>A key that moves from E:\ to F:\ is still the same key.</summary>
    [Fact]
    public void RecognisesADestinationByItsMarkerRatherThanItsPath()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        var marker = SinkMarker.Write(first.Path, "Clé du cabinet");

        Assert.Equal(marker.SinkId, SinkMarker.Read(first.Path)!.SinkId);
        Assert.Equal("Clé du cabinet", SinkMarker.Read(first.Path)!.Label);
        Assert.Null(SinkMarker.Read(second.Path));
    }

    [Fact]
    public void KeepsTheNewestSnapshotsAndOneADay()
    {
        using var directory = new TempDirectory();
        using var vault = VaultManager.Create(directory.Path, new FakeDeviceKeyStore()).Vault;

        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

        // Four today, then one a day going back a fortnight.
        foreach (var offset in new[] { 0, -1, -2, -3 })
        {
            Touch(vault, now.AddHours(offset));
        }

        for (var day = 1; day <= 14; day++)
        {
            Touch(vault, now.AddDays(-day));
        }

        var store = vault.Snapshots;
        Assert.Equal(18, store.List().Count);

        store.Prune(new SnapshotRetention(KeepNewest: 2, KeepDailyForDays: 7), now);

        var kept = store.List();

        // The two newest, plus the last of each of the seven days inside the window.
        Assert.Equal(9, kept.Count);
        Assert.Equal(now, kept[0].TakenAt);
        Assert.DoesNotContain(kept, snapshot => snapshot.TakenAt < now.AddDays(-7));
    }

    private static IBackupSink Sink(TempDirectory directory) =>
        new DirectorySink(new FixedPathLocator(directory.Path, "Test"));

    private static void Touch(OpenVault vault, DateTimeOffset takenAt) =>
        File.WriteAllText(
            Path.Combine(vault.Paths.BackupsDirectory, $"{takenAt.UtcDateTime:yyyyMMdd-HHmmss}-auto.db"),
            "snapshot");

    private static void Execute(OpenVault vault, string sql)
    {
        using var connection = vault.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(OpenVault vault, string sql)
    {
        using var connection = vault.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }
}
