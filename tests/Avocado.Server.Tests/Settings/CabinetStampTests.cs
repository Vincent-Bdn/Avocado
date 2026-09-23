using Avocado.Server.Features.Documents.Folders;

namespace Avocado.Server.Tests.Settings;

/// <summary>
/// What a cabinet may hand over as a stamp, and what the SVG route promises.
/// </summary>
public class CabinetStampTests
{
    [Theory]
    [InlineData("tampon.svg", true)]
    [InlineData("TAMPON.SVG", true)]
    [InlineData("logo.png", true)]
    [InlineData("logo.jpg", true)]
    [InlineData("logo.jpeg", true)]
    [InlineData("logo.webp", false)]
    [InlineData("logo.pdf", false)]
    [InlineData("logo.eps", false)]
    public void SaysWhatCanBeAStamp(string fileName, bool expected) =>
        Assert.Equal(expected, CabinetStamp.IsAnImage(fileName));

    [Theory]
    [InlineData("tampon.svg", true)]
    [InlineData("tampon.SVG", true)]
    // « .svgz » is gzipped and the window cannot read it as text, so it is not the same thing.
    [InlineData("tampon.svgz", false)]
    [InlineData("tampon.png", false)]
    public void TellsAnSvgApartFromAnImage(string fileName, bool expected) =>
        Assert.Equal(expected, CabinetStamp.IsSvg(fileName));

    /// <summary>
    /// The placeholder is a contract with three other places: the wording in Réglages, the prompt she
    /// pastes into an assistant, and the code in the window that looks for it. Changing it here and
    /// nowhere else would leave every cabinet's SVG silently unplaceable, so it is pinned.
    /// </summary>
    [Fact]
    public void KeepsThePlaceholderTheOneEveryoneWasTold() =>
        Assert.Equal("{{numero}}", CabinetStamp.Placeholder);

    /// <summary>
    /// The colour is what the SVG's own placeholder was drawn in, so it has to survive the row it is
    /// stored in. It did not exist until recently and an older row has none.
    /// </summary>
    [Fact]
    public void CarriesTheNumberColourThroughStorage()
    {
        var stored = CabinetStamp.Serialise(new StampOptions(NumberColour: "#093126"));

        Assert.Contains("093126", stored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A raster is stored as she gave it; only an SVG is redrawn.</summary>
    [Fact]
    public void KeepsRasterAndVectorListsInStep()
    {
        Assert.All(CabinetStamp.RasterExtensions, extension =>
            Assert.Contains(extension, CabinetStamp.Extensions));

        Assert.DoesNotContain(".svg", CabinetStamp.RasterExtensions);
    }
}
