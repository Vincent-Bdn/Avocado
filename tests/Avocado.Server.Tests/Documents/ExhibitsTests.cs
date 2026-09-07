using Avocado.Server.Features.Documents.Folders;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// Which number a pièce takes, and what its file is called.
///
/// <para>Worth its own tests because a pièce number is cited in conclusions filed with a court. Once
/// communicated it is a reference somebody else relies on, and handing the same number to a different
/// document later would make those conclusions point at the wrong thing.</para>
/// </summary>
public class ExhibitsTests
{
    [Fact]
    public void StartsAtOneInAnEmptyFolder() => Assert.Equal(1, Exhibits.NextNumber([]));

    [Fact]
    public void TakesTheNextOneAfterAFullRun() =>
        Assert.Equal(4, Exhibits.NextNumber([
            "Pièce 1 - Contrat.pdf",
            "Pièce 2 - Mise en demeure.pdf",
            "Pièce 3 - Attestation.pdf",
        ]));

    /// <summary>
    /// The one that matters, and the one I got backwards first time.
    ///
    /// <para>Pièce 2 was withdrawn. Its number may already appear in conclusions filed with a court,
    /// so handing it to the next document versé would make them cite something else, silently, weeks
    /// later. The counter only goes up.</para>
    /// </summary>
    [Fact]
    public void NeverReusesAWithdrawnNumberOnItsOwn() =>
        Assert.Equal(4, Exhibits.NextNumber(["Pièce 1 - Contrat.pdf", "Pièce 3 - Attestation.pdf"]));

    /// <summary>And the hole is offered back, to be filled deliberately or not at all.</summary>
    [Fact]
    public void OffersTheWithdrawnNumbersBack() =>
        Assert.Equal([2], Exhibits.FreeNumbers(["Pièce 1 - Contrat.pdf", "Pièce 3 - Attestation.pdf"]));

    [Fact]
    public void HasNoFreeNumbersWhenNothingWasWithdrawn() =>
        Assert.Empty(Exhibits.FreeNumbers(["Pièce 1 - Contrat.pdf", "Pièce 2 - Attestation.pdf"]));

    [Fact]
    public void IgnoresWhateverElseIsInTheFolder() =>
        Assert.Equal(1, Exhibits.NextNumber(["Bordereau.docx", "Pièces jointes.pdf", "notes.txt"]));

    [Theory]
    [InlineData("Pièce 12 - Attestation.pdf", 12)]
    [InlineData("pièce 7 - minuscule.pdf", 7)]
    [InlineData("Pièce 3.pdf", 3)]
    [InlineData("Pièces jointes.pdf", null)]
    [InlineData("Pièce - sans numéro.pdf", null)]
    [InlineData("Assignation.pdf", null)]
    public void ReadsTheNumberOffTheName(string fileName, int? expected) =>
        Assert.Equal(expected, Exhibits.NumberOf(fileName));

    [Fact]
    public void NamesItFromTheLibelle() =>
        Assert.Equal(
            "Pièce 4 - Attestation de M. Loubet.pdf",
            Exhibits.FileName(4, "Attestation de M. Loubet", "scan0043.pdf"));

    /// <summary>No libellé yet is a normal state: the file's own name says something meanwhile.</summary>
    [Fact]
    public void FallsBackToTheFileNameWhenThereIsNoLibelle() =>
        Assert.Equal("Pièce 4 - contrat-2019.pdf", Exhibits.FileName(4, null, "contrat-2019.pdf"));

    /// <summary>
    /// « Contrat du 3/03/2019 : avenants » is a perfectly good libellé and Windows refuses three of
    /// those characters. They become spaces so words do not run together.
    /// </summary>
    [Fact]
    public void TurnsWhatWindowsRefusesIntoSpaces() =>
        Assert.Equal(
            "Pièce 9 - Contrat du 3 03 2019 avenants.pdf",
            Exhibits.FileName(9, "Contrat du 3/03/2019 : avenants", "x.pdf"));

    /// <summary>A path Windows will not open is worse than a name she has to read the rest of inside.</summary>
    [Fact]
    public void CapsALibelleThatIsReallyAParagraph()
    {
        var name = Exhibits.FileName(1, new string('a', 300), "x.pdf");

        Assert.True(name.Length <= 110, name.Length.ToString());
        Assert.EndsWith(".pdf", name);
    }

    [Theory]
    [InlineData("Attestation.", "Attestation")]
    [InlineData("Attestation ", "Attestation")]
    [InlineData("  deux   espaces  ", "deux espaces")]
    public void TrimsWhatWindowsWouldRefuseAtCreation(string label, string expected) =>
        Assert.Equal(expected, Exhibits.Safe(label));

    /// <summary>A libellé of nothing but punctuation still has to produce a file name.</summary>
    [Fact]
    public void StillNamesItWhenTheLibelleSurvivesToNothing() =>
        Assert.Equal("Pièce 5.pdf", Exhibits.FileName(5, "///", "///.pdf"));
}
