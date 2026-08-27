using Avocado.Server.Features.Settings.Infrastructure;

namespace Avocado.Server.Tests.Settings;

/// <summary>
/// Whose message it is, which is the whole of « reçu » versus « envoyé ».
///
/// <para>Nothing recorded who she was, so every message went into the journal as reçu. On the real
/// export that is 720 of 1 317 messages filed as arriving when she had sent them, and there is no
/// clever way to infer it: an earlier attempt asked whether the sender was in the carnet, which
/// answers a different question and gave the same answer for every message anyway.</para>
/// </summary>
public class PracticeAddressesTests
{
    [Theory]
    [InlineData("maitre@cabinet.fr")]
    [InlineData("  Maitre@Cabinet.FR  ")]
    [InlineData("secretariat@cabinet.fr")]
    public void KeepsAnAddress(string typed) =>
        Assert.Single(PracticeAddresses.Clean([typed]).Accepted);

    /// <summary>
    /// The real export carries messages from eight people at @cvs-avocats.com. Listing them one by
    /// one is tedious on the day and wrong the day a colleague arrives.
    /// </summary>
    [Fact]
    public void KeepsAWholeDomain() =>
        Assert.Equal("@cvs-avocats.com", Assert.Single(PracticeAddresses.Clean(["@cvs-avocats.com"]).Accepted));

    [Theory]
    [InlineData("pas une adresse")]
    [InlineData("cvs-avocats.com")]
    [InlineData("a@b@c")]
    [InlineData("maitre@")]
    public void RefusesWhatIsNotOne(string typed)
    {
        var cleaned = PracticeAddresses.Clean([typed]);

        Assert.Empty(cleaned.Accepted);
        Assert.NotEmpty(cleaned.Rejected);
    }

    /// <summary>Lower-cased, because matching must never turn on how somebody capitalised it.</summary>
    [Fact]
    public void LowerCasesWhatItKeeps() =>
        Assert.Equal("maitre@cabinet.fr", Assert.Single(PracticeAddresses.Clean(["Maitre@Cabinet.FR"]).Accepted));

    /// <summary>She will paste a line with commas in it, and that is not an error.</summary>
    [Fact]
    public void SplitsWhateverSheSeparatedThemWith() =>
        Assert.Equal(
            ["a@cabinet.fr", "b@cabinet.fr", "@cabinet.fr"],
            PracticeAddresses.Clean(["a@cabinet.fr, b@cabinet.fr; @cabinet.fr"]).Accepted);

    [Fact]
    public void KeepsEachOnlyOnce() =>
        Assert.Single(PracticeAddresses.Clean(["a@cabinet.fr", "A@CABINET.FR"]).Accepted);

    [Theory]
    [InlineData("ppascaudblandin@cvs-avocats.com", true)]
    [InlineData("PPascaudBlandin@CVS-Avocats.com", true)]
    [InlineData("lboudiarobin@cvs-avocats.com", true)]
    [InlineData("direction@perles-diffusion.fr", false)]
    [InlineData("someone@notcvs-avocats.com", false)]
    public void ADomainCoversEveryoneOnIt(string sender, bool hers) =>
        Assert.Equal(hers, PracticeAddresses.IsOwn(sender, ["@cvs-avocats.com"]));

    [Theory]
    [InlineData("maitre@cabinet.fr", true)]
    [InlineData("autre@cabinet.fr", false)]
    public void AnAddressCoversOnlyItself(string sender, bool hers) =>
        Assert.Equal(hers, PracticeAddresses.IsOwn(sender, ["maitre@cabinet.fr"]));

    /// <summary>Nothing said means nothing known, and everything stays « reçu ».</summary>
    [Fact]
    public void ClaimsNothingWhenSheHasSaidNothing() =>
        Assert.False(PracticeAddresses.IsOwn("maitre@cabinet.fr", []));

    /// <summary>Some of Gestisoft's exports carry no sender at all, and drafts never do.</summary>
    [Fact]
    public void ClaimsNothingForAMessageWithNoSender() =>
        Assert.False(PracticeAddresses.IsOwn(string.Empty, ["@cabinet.fr"]));

    [Fact]
    public void ReadsBackWhatWasStored() =>
        Assert.Equal(
            ["@cabinet.fr", "maitre@gmail.com"],
            PracticeAddresses.Parse("@cabinet.fr\nmaitre@gmail.com\n\n"));
}
