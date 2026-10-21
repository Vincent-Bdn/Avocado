using Avocado.Server.Features.Backups.Infrastructure;

namespace Avocado.Server.Tests.Backups;

/// <summary>
/// The judgement that decides whether the interface is allowed to tell someone they are safe. It got
/// this wrong in production once, announcing « vous ne perdriez rien » to a practice whose only copy
/// was a folder beside the vault, so every branch is pinned here.
/// </summary>
public class DestinationReachTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"avocado-reach-{Guid.NewGuid():N}");

    public DestinationReachTests() => Directory.CreateDirectory(Vault);

    private string Vault => Path.Combine(_root, "coffre");

    [Fact]
    public void AFolderInsideTheVaultIsRefused()
    {
        var verdict = DestinationReachInspector.Inspect(Path.Combine(Vault, "backups"), Vault);

        Assert.Equal(DestinationReach.InsideVault, verdict.Reach);
        Assert.False(verdict.IsOffMachine);
    }

    [Fact]
    public void TheVaultFolderItselfIsRefused()
    {
        Assert.Equal(DestinationReach.InsideVault, DestinationReachInspector.Inspect(Vault, Vault).Reach);
    }

    /// <summary>
    /// A NAS, addressed the way anyone addresses one on Windows.
    ///
    /// <para><c>DriveInfo</c> throws on a UNC path, the catch swallowed it, and the cautious fallback
    /// then announced « ce dossier est sur cet ordinateur… les deux copies partiraient ensemble » to a
    /// practice whose backups were landing on a share in another room. A mapped drive letter was
    /// classified correctly all along, which is how this survived: it only misfired for the people who
    /// typed the share directly.</para>
    ///
    /// <para><b>And the answer differs by platform, because the question does.</b> A UNC path is a
    /// Windows spelling. On a Mac or on Linux every character of <c>\\NAS\partage</c> is legal in a
    /// file name, so that string is a relative local path and « same machine » is the correct verdict
    /// rather than a missed case. A share really mounted on a Mac is <c>/Volumes/NAS/…</c>, looks like
    /// any other absolute path, and is caught by the <c>DriveType.Network</c> branch instead — which
    /// is why that branch stays and this one is additional.</para>
    ///
    /// <para>Asserted on both sides rather than skipped off Windows: a test that returns early says
    /// nothing, and what it would be hiding here is a real difference worth writing down.</para>
    /// </summary>
    [Theory]
    [InlineData(@"\\NAS\partage\Sauvegardes")]
    [InlineData(@"\\192.168.1.40\avocado")]
    [InlineData(@"\\NAS\partage")]
    [InlineData("//NAS/partage/Sauvegardes")]
    public void AShareIsOffMachineHoweverItIsSpelledOnWindows(string path)
    {
        var verdict = DestinationReachInspector.Inspect(path, Vault);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DestinationReach.OffMachine, verdict.Reach);
            Assert.True(verdict.IsOffMachine);
            Assert.Contains("réseau", verdict.Detail);

            return;
        }

        Assert.Equal(DestinationReach.SameMachine, verdict.Reach);
    }

    /// <summary>
    /// A rooted local path is not a share. One leading separator means « this drive », and reading it
    /// as a NAS would hand out the reassurance the whole type exists to withhold.
    /// </summary>
    [Fact]
    public void ARootedLocalPathIsNotAShare()
    {
        Assert.NotEqual(
            DestinationReach.OffMachine,
            DestinationReachInspector.Inspect(Path.Combine(_root, "ailleurs"), Vault).Reach);
    }

    /// <summary>
    /// A sibling whose name merely starts the same way is a perfectly good destination. Matching on a
    /// string prefix rather than on path segments would refuse it.
    /// </summary>
    [Fact]
    public void ASiblingWithASimilarNameIsNotInsideTheVault()
    {
        var sibling = Path.Combine(_root, "coffre-sauvegardes");
        Directory.CreateDirectory(sibling);

        Assert.NotEqual(DestinationReach.InsideVault, DestinationReachInspector.Inspect(sibling, Vault).Reach);
    }

    [Fact]
    public void AnOrdinaryFolderOnThisDiskIsAWarning()
    {
        var elsewhere = Path.Combine(_root, "sauvegardes");
        Directory.CreateDirectory(elsewhere);

        var verdict = DestinationReachInspector.Inspect(elsewhere, Vault);

        Assert.Equal(DestinationReach.SameMachine, verdict.Reach);
        Assert.False(verdict.IsOffMachine);

        // The message has to say what it does protect against, not only what it does not, since the
        // user is being asked to make a judgement we cannot make for them.
        Assert.Contains("fausse manœuvre", verdict.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The arrangement the setup wizard recommends. A synced folder normally sits on the internal
    /// disk, so testing the drive before the sync markers would classify the recommendation as
    /// useless, which is why the order in the inspector is deliberate.
    /// </summary>
    [Theory]
    [InlineData("OneDrive")]
    [InlineData("Dropbox")]
    [InlineData("Google Drive")]
    public void AFolderInsideASyncClientIsOffMachine(string client)
    {
        var synced = Path.Combine(_root, client, "Sauvegardes Avocado");
        Directory.CreateDirectory(synced);

        var verdict = DestinationReachInspector.Inspect(synced, Vault);

        Assert.Equal(DestinationReach.OffMachine, verdict.Reach);
        Assert.True(verdict.IsOffMachine);
        Assert.NotNull(verdict.SyncRoot);
    }

    /// <summary>Inside the vault beats everything: a synced folder in there is still a bad idea.</summary>
    [Fact]
    public void InsideTheVaultWinsOverASyncedName()
    {
        var inside = Path.Combine(Vault, "Dropbox");
        Directory.CreateDirectory(inside);

        Assert.Equal(DestinationReach.InsideVault, DestinationReachInspector.Inspect(inside, Vault).Reach);
    }

    [Fact]
    public void EveryVerdictCarriesSomethingToShow()
    {
        var elsewhere = Path.Combine(_root, "ailleurs");
        Directory.CreateDirectory(elsewhere);

        foreach (var verdict in new[]
                 {
                     DestinationReachInspector.Inspect(Vault, Vault),
                     DestinationReachInspector.Inspect(elsewhere, Vault),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(verdict.Detail));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
