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

    /// <param name="suffix">Null for an ordinary pièce; the whole point of the type otherwise.</param>
    [Theory]
    [InlineData("Pièce 12 - Attestation.pdf", 12, null)]
    [InlineData("pièce 7 - minuscule.pdf", 7, null)]
    [InlineData("Pièce 3.pdf", 3, null)]
    [InlineData("Pièce 68 bis - Avenant.pdf", 68, "bis")]
    [InlineData("Pièce 68 ter - Second avenant.pdf", 68, "ter")]
    [InlineData("Pièce 68-1 - Annexe.pdf", 68, "1")]
    [InlineData("Pièce 68-2.pdf", 68, "2")]
    [InlineData("Pièce 68 bis.pdf", 68, "bis")]
    [InlineData("Pièces jointes.pdf", null, null)]
    [InlineData("Pièce - sans numéro.pdf", null, null)]
    [InlineData("Assignation.pdf", null, null)]
    public void ReadsTheNumberOffTheName(string fileName, int? number, string? suffix) =>
        Assert.Equal(
            number is null ? null : new ExhibitNumber(number.Value, suffix),
            Exhibits.NumberOf(fileName));

    /// <summary>
    /// The libellé separator is « - » with a space on each side, and a suffix never has one. Reading
    /// the libellé as a suffix would make every pièce ever versed a suffixed one.
    /// </summary>
    [Fact]
    public void NeverReadsTheLibelleAsASuffix() =>
        Assert.Null(Exhibits.NumberOf("Pièce 12 - Attestation.pdf")!.Value.Suffix);

    /// <summary>« 68 bis » for a word, « 68-1 » for a figure: both spellings are hers.</summary>
    [Theory]
    [InlineData(12, null, "12")]
    [InlineData(68, "bis", "68 bis")]
    [InlineData(68, "1", "68-1")]
    public void WritesTheNumberTheWayItIsCited(int number, string? suffix, string expected) =>
        Assert.Equal(expected, new ExhibitNumber(number, suffix).ToString());

    /// <summary>
    /// A « 68 bis » claims 68 as firmly as a plain one does: it exists because 68 did, and handing 68
    /// to something else afterwards is the mistake this type exists to prevent.
    /// </summary>
    [Fact]
    public void CountsASuffixedPieceAsClaimingItsNumber()
    {
        Assert.Equal(69, Exhibits.NextNumber(["Pièce 68 bis - Avenant.pdf"]));
        Assert.Empty(Exhibits.FreeNumbers(["Pièce 1.pdf", "Pièce 2 bis - x.pdf"]));
    }

    /// <summary>But « 68 bis » itself is free on a dossier that has « 68 ». That is what it is for.</summary>
    [Fact]
    public void LeavesTheSuffixedNumberFreeUntilSomethingCarriesIt()
    {
        string[] versed = ["Pièce 68 - Contrat.pdf"];

        Assert.False(Exhibits.IsTaken(versed, new ExhibitNumber(68, "bis")));
        Assert.True(Exhibits.IsTaken(versed, new ExhibitNumber(68)));
        Assert.True(Exhibits.IsTaken(["Pièce 68 bis - x.pdf"], new ExhibitNumber(68, "bis")));
    }

    /// <summary>
    /// A suffix lands between the number and the libellé separator, so a space or a dash inside it
    /// would make the pièce's own number unreadable.
    /// </summary>
    [Theory]
    [InlineData("bis", "bis")]
    [InlineData("BIS", "bis")]
    [InlineData(" bis ", "bis")]
    [InlineData("bis - 2", "bis2")]
    [InlineData("///", null)]
    [InlineData("", null)]
    public void KeepsOnlyWhatCanGoInAFileName(string suffix, string? expected) =>
        Assert.Equal(expected, Exhibits.CleanSuffix(suffix));

    /// <summary>
    /// Both folder names are read and only the new one is written. Every pièce versed until now is
    /// in « Pièces », and those numbers are cited in documents that have been filed.
    /// </summary>
    [Fact]
    public void WritesToItsOwnFolderAndReadsTheOldOneToo()
    {
        Assert.Equal("Pièces Avocado", Exhibits.Folder);
        Assert.Equal("Pièces", Exhibits.LegacyFolder);
        Assert.Equal(["Pièces Avocado", "Pièces"], Exhibits.Folders);
    }

    [Fact]
    public void NamesItFromTheLibelle() =>
        Assert.Equal(
            "Pièce 4 - Attestation de M. Loubet.pdf",
            Exhibits.FileName(new ExhibitNumber(4), "Attestation de M. Loubet", "scan0043.pdf"));

    /// <summary>No libellé yet is a normal state: the file's own name says something meanwhile.</summary>
    [Fact]
    public void FallsBackToTheFileNameWhenThereIsNoLibelle() =>
        Assert.Equal("Pièce 4 - contrat-2019.pdf", Exhibits.FileName(new ExhibitNumber(4), null, "contrat-2019.pdf"));

    /// <summary>
    /// « Pièce 1 - Pièce 1 - Photos.pdf », which is what a dossier filed last week actually carries.
    ///
    /// <para>Versing a file she had already numbered by hand, or versing the same one twice, put the
    /// prefix on a name that already had one. The number is re-applied, so the one in the name is the
    /// one this versement decided, not the one it inherited.</para>
    /// </summary>
    [Theory]
    [InlineData("Pièce 1 - Photos.pdf", "Pièce 4 - Photos.pdf")]
    [InlineData("Pièce 12 - Attestation de M. Loubet.pdf", "Pièce 4 - Attestation de M. Loubet.pdf")]
    public void DoesNotPrefixANameThatIsAlreadyAPiece(string source, string expected) =>
        Assert.Equal(expected, Exhibits.FileName(new ExhibitNumber(4), null, source));

    /// <summary>
    /// « Pièces jointes.pdf » starts with the word and is not one: no number, so nothing comes off.
    /// </summary>
    [Fact]
    public void LeavesANameThatMerelyStartsWithTheWordAlone() =>
        Assert.Equal("Pièce 4 - Pièces jointes.pdf", Exhibits.FileName(new ExhibitNumber(4), null, "Pièces jointes.pdf"));

    /// <summary>
    /// « Contrat du 3/03/2019 : avenants » is a perfectly good libellé and Windows refuses three of
    /// those characters. They become spaces so words do not run together.
    /// </summary>
    [Fact]
    public void TurnsWhatWindowsRefusesIntoSpaces() =>
        Assert.Equal(
            "Pièce 9 - Contrat du 3 03 2019 avenants.pdf",
            Exhibits.FileName(new ExhibitNumber(9), "Contrat du 3/03/2019 : avenants", "x.pdf"));

    /// <summary>A path Windows will not open is worse than a name she has to read the rest of inside.</summary>
    [Fact]
    public void CapsALibelleThatIsReallyAParagraph()
    {
        var name = Exhibits.FileName(new ExhibitNumber(1), new string('a', 300), "x.pdf");

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
        Assert.Equal("Pièce 5.pdf", Exhibits.FileName(new ExhibitNumber(5), "///", "///.pdf"));
}
