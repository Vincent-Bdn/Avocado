using Avocado.Server.Features.Documents.Folders;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// These assert the same thing on every runner, which is the entire point.
///
/// <para><see cref="Path.GetInvalidFileNameChars"/> answers for the machine it runs on: nine
/// characters on Windows, two on macOS and Linux. Every name Avocado writes goes into a folder of
/// hers that will be restored onto another machine, copied to a colleague, or opened on the office
/// PC after being made on the laptop, so the strictest rule has to apply everywhere.</para>
/// </summary>
public class PortableFileNameTests
{
    [Theory]
    [InlineData(':')]   // Legal on macOS and Linux. Refused by Windows, and swapped with / by Finder.
    [InlineData('?')]
    [InlineData('*')]
    [InlineData('"')]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData('|')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('\0')]
    public void RefusesWhatAnyOfThePlatformsRefuses(char character) =>
        Assert.True(PortableFileName.IsInvalid(character), $"U+{(int)character:X4} was allowed through.");

    [Theory]
    [InlineData('é')]
    [InlineData('è')]
    [InlineData('ç')]
    [InlineData('\'')]
    [InlineData('-')]
    [InlineData('°')]
    [InlineData(' ')]
    public void LeavesAloneWhatAFrenchFileNameIsFullOf(char character) =>
        Assert.False(PortableFileName.IsInvalid(character), $"'{character}' was replaced.");

    /// <summary>
    /// The one that was actually broken. On a Mac this used to keep the colon, and eight of the ten
    /// practices in the beta are on Macs.
    /// </summary>
    [Fact]
    public void MakesTheSameNameOnAMacAsOnAPc() =>
        Assert.Equal(
            "Pièce 9 - Contrat du 3 03 2019 avenants.pdf",
            Exhibits.FileName(9, "Contrat du 3/03/2019 : avenants", "x.pdf"));

    [Fact]
    public void RefusesToEndANameWindowsWouldNotCreate()
    {
        // Legal in a string, refused by Windows at creation, and silently truncated by some tools.
        Assert.Equal("Attestation", PortableFileName.Clean("Attestation.", '-'));
        Assert.Equal("Attestation", PortableFileName.Clean("Attestation ", '-'));
    }
}
