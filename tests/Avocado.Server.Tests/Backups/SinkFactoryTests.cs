using Avocado.Server.Features.Backups;
using Avocado.Server.Features.Backups.Endpoints;
using Avocado.Server.Features.Backups.Infrastructure;

namespace Avocado.Server.Tests.Backups;

public class SinkFactoryTests
{
    private readonly SinkFactory _factory = new();

    [Fact]
    public void BuildsAFolderSink()
    {
        var sink = _factory.Create(new BackupDestination
        {
            Kind = BackupDestinationKinds.Folder,
            Label = "Disque externe",
            Path = Path.GetTempPath(),
        });

        Assert.NotNull(sink);
        Assert.Equal("Disque externe", sink.DisplayName);
    }

    [Fact]
    public void BuildsAVolumeSinkFromItsMarkerId()
    {
        var sink = _factory.Create(new BackupDestination
        {
            Kind = BackupDestinationKinds.Volume,
            Label = "Clé du cabinet",
            VolumeId = Guid.NewGuid(),
        });

        Assert.NotNull(sink);
    }

    /// <summary>
    /// A row written by a newer version has to degrade to "one destination I do not understand"
    /// rather than stopping the server, which is the whole reason Kind is text and this returns null.
    /// </summary>
    [Theory]
    [InlineData("s3")]
    [InlineData("googleDrive")]
    [InlineData("")]
    public void ReturnsNothingForAKindItDoesNotKnow(string kind)
    {
        var destination = new BackupDestination { Kind = kind, Label = "Inconnu" };

        Assert.Null(_factory.Create(destination));
        Assert.False(string.IsNullOrWhiteSpace(_factory.ExplainMissing(destination)));
    }

    /// <summary>Half-configured rows are the same case: no sink, and a sentence saying why.</summary>
    [Fact]
    public void ReturnsNothingWhenTheConfigurationIsIncomplete()
    {
        Assert.Null(_factory.Create(new BackupDestination { Kind = BackupDestinationKinds.Folder, Path = null }));
        Assert.Null(_factory.Create(new BackupDestination { Kind = BackupDestinationKinds.Volume, VolumeId = null }));
    }

    /// <summary>
    /// Someone who asks for Google Drive should be told what to do instead, not that it is unsupported.
    /// </summary>
    [Fact]
    public void PointsGoogleDriveAtTheDesktopClient()
    {
        var detail = _factory.ExplainMissing(new BackupDestination { Kind = "googleDrive" });

        Assert.Contains("Google Drive pour ordinateur", detail, StringComparison.Ordinal);
    }
}

public class BackupDestinationInputTests
{
    private static BackupDestinationInput Valid => new("folder", "Sauvegarde", @"C:\ailleurs");

    [Fact]
    public void AcceptsAWellFormedDestination() => Assert.Null(Valid.Validate());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesADestinationWithNoName(string label) =>
        Assert.NotNull((Valid with { Label = label }).Validate());

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesADestinationWithNoFolder(string? path) =>
        Assert.NotNull((Valid with { Path = path }).Validate());

    /// <summary>A retention that keeps nothing is never what anyone meant.</summary>
    [Fact]
    public void RefusesARetentionThatKeepsNothing() =>
        Assert.NotNull((Valid with { KeepNewest = 0 }).Validate());

    [Fact]
    public void DoesNotAcceptSameMachineByDefault() => Assert.False(Valid.AcceptSameMachine);
}
