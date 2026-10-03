using Avocado.Vault.Backups;

namespace Avocado.Vault.Tests;

/// <summary>
/// Finding a destination, which is a different question from writing to one.
///
/// <para>These are about a share that is not mounted. A cabinet's NAS reached over Tailscale is a
/// folder under /Volumes on a Mac and a letter on Windows, and it is absent far more often than a
/// USB key: the laptop sleeps, the tunnel drops, the share is not remounted at login.</para>
/// </summary>
public class SinkLocatorTests
{
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            Directory.CreateDirectory(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"avocado-locator-{Guid.NewGuid():N}")).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task FindsAFolderThatIsThere()
    {
        using var folder = new TempFolder();

        Assert.Equal(
            System.IO.Path.GetFullPath(folder.Path),
            await new FixedPathLocator(folder.Path, "Test").LocateAsync());
    }

    /// <summary>
    /// The one that would have put a cabinet's backups on the wrong disk.
    ///
    /// <para>It used to accept a missing folder as long as the parent existed, so the first write
    /// could create it. An unmounted share leaves its parent perfectly present: /Volumes on a Mac,
    /// /mnt on Linux, the drive's root on Windows. The folder would then be created on the local
    /// disk and every copy written there, while Réglages reported a destination working normally and
    /// « dernière copie » kept moving forward.</para>
    /// </summary>
    [Fact]
    public async Task RefusesAFolderThatIsNotThereEvenWhenItsParentIs()
    {
        using var parent = new TempFolder();

        var missing = Path.Combine(parent.Path, "nas-pas-monte");

        Assert.Null(await new FixedPathLocator(missing, "NAS").LocateAsync());
        Assert.False(Directory.Exists(missing), "Looking for a destination must not create one.");
    }

    [Fact]
    public async Task NarrowsToASubfolderAndMakesIt()
    {
        using var folder = new TempFolder();

        var located = await new SubfolderLocator(
            new FixedPathLocator(folder.Path, "NAS"), "Sauvegardes/Avocado").LocateAsync();

        Assert.Equal(Path.Combine(folder.Path, "Sauvegardes", "Avocado"), located);
    }

    /// <summary>Absent stays absent: a subfolder of nowhere is nowhere, not a folder to create.</summary>
    [Fact]
    public async Task StaysAbsentWhenTheDeviceIsNotThere()
    {
        using var parent = new TempFolder();

        var locator = new SubfolderLocator(
            new FixedPathLocator(Path.Combine(parent.Path, "absent"), "NAS"), "Sauvegardes");

        Assert.Null(await locator.LocateAsync());
    }

    [Theory]
    [InlineData("Sauvegardes", "Sauvegardes")]
    [InlineData("/Sauvegardes/Avocado/", "Sauvegardes|Avocado")]
    [InlineData("Sauvegardes\\Avocado", "Sauvegardes|Avocado")]
    [InlineData("  Sauvegardes / Avocado ", "Sauvegardes|Avocado")]
    [InlineData("", "")]
    [InlineData(null, "")]
    // « .. » is how a stored row would climb off the device it was found on.
    [InlineData("../../ailleurs", "ailleurs")]
    [InlineData("./Sauvegardes", "Sauvegardes")]
    public void TidiesWhateverShapeTheSubpathWasTypedIn(string? typed, string expected) =>
        Assert.Equal(
            expected.Replace('|', Path.DirectorySeparatorChar),
            SubfolderLocator.Clean(typed));
}
