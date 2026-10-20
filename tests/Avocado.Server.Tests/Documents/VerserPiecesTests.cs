using Microsoft.AspNetCore.Http;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Matters;
using PdfSharp.Pdf;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// Versement en lot: a selection of documents becomes numbered pièces with their bordereau.
///
/// <para>The numbering is the dangerous part. A pièce is cited by its number in conclusions that have
/// been filed, so a number that moves, repeats, or is silently consumed by a file that was not
/// actually written is not a display bug: it is an argument that no longer points at anything.</para>
///
/// <para>These drive the endpoint through <see cref="TestVault"/> and a real folder on disk, because
/// what is being tested is what ends up in Pièces/.</para>
/// </summary>
public sealed class VerserPiecesTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avocado-verser-" + Guid.NewGuid().ToString("N"));

    public VerserPiecesTests() => Directory.CreateDirectory(_root);

    private Matter Dossier() => _vault.Save(new Matter
    {
        Reference = "2026-0001",
        Name = "Durand contre Martin",
        OpenedOn = new DateOnly(2026, 1, 5),
        CourtCaseNumber = "23/04567",
        DocumentsFolder = _root,
    });

    private string Pdf(string name, int pages = 1)
    {
        using var document = new PdfDocument();

        for (var index = 0; index < pages; index++)
        {
            document.AddPage();
        }

        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);

        return name;
    }

    private string Pieces(string name) => Path.Combine(_root, Exhibits.Folder, name);

    private async Task<VerserPiecesResult> Verse(Matter matter, VerserPiecesInput input)
    {
        var result = await VerserPieces.HandleAsync(
            matter.Id, input, _vault.Database, new NoVault(), default);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        return Assert.IsType<VerserPiecesResult>(ok.Value);
    }

    [Fact]
    public async Task NumbersThemInTheOrderSheChose()
    {
        var matter = Dossier();
        var third = Pdf("Assignation.pdf");
        var first = Pdf("Contrat.pdf");
        var second = Pdf("Mise en demeure.pdf");

        // Deliberately not alphabetical and not by date: a bordereau's order is an argument.
        var result = await Verse(matter, new VerserPiecesInput(
        [
            new PieceToVerse(first, "Contrat du 3 mars 2019"),
            new PieceToVerse(second, "Mise en demeure"),
            new PieceToVerse(third, "Assignation"),
        ], Stamp: false));

        Assert.Equal(0, result.Failed);
        Assert.Equal(["1", "2", "3"], result.Pieces.Select(piece => piece.Number));
        Assert.True(File.Exists(Pieces("Pièce 1 - Contrat du 3 mars 2019.pdf")));
        Assert.True(File.Exists(Pieces("Pièce 2 - Mise en demeure.pdf")));
        Assert.True(File.Exists(Pieces("Pièce 3 - Assignation.pdf")));
    }

    [Fact]
    public async Task CarriesOnPastTheHighestNumberAlreadyUsed()
    {
        var matter = Dossier();
        Directory.CreateDirectory(Path.Combine(_root, Exhibits.Folder));
        await File.WriteAllTextAsync(Pieces("Pièce 7 - Déjà versée.pdf"), "x");

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Nouvelle.pdf"), "Nouvelle")], Stamp: false));

        Assert.Equal("8", result.Pieces[0].Number);
    }

    [Fact]
    public async Task NeverFillsAFreedNumber()
    {
        // Pièce 2 was withdrawn. Its number stays spent, because conclusions already filed cite
        // « la pièce n° 2 » and giving it to something else makes that citation point elsewhere.
        var matter = Dossier();
        Directory.CreateDirectory(Path.Combine(_root, Exhibits.Folder));
        await File.WriteAllTextAsync(Pieces("Pièce 1 - Une.pdf"), "x");
        await File.WriteAllTextAsync(Pieces("Pièce 3 - Trois.pdf"), "x");

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Quatre.pdf"), "Quatre")], Stamp: false));

        Assert.Equal("4", result.Pieces[0].Number);
    }

    [Fact]
    public async Task StartsWhereSheSaysWhenSheSaysSo()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Une.pdf"), "Une"), new PieceToVerse(Pdf("Deux.pdf"), "Deux")],
            StartNumber: 20,
            Stamp: false));

        Assert.Equal(["20", "21"], result.Pieces.Select(piece => piece.Number));
    }

    /// <summary>
    /// « Option d'ajouter bis, ex. Pièce 68 bis, ou 68-1 68-2. »
    ///
    /// <para>A suffixed pièce stays on the number of the line before it instead of consuming a new
    /// one, which is what bis means: this document belongs with 68. Something that arrives after the
    /// bordereau was sent cannot take 69, which is already cited as something else.</para>
    /// </summary>
    [Fact]
    public async Task KeepsTheNumberForASuffixedPiece()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
        [
            new PieceToVerse(Pdf("Contrat.pdf"), "Contrat"),
            new PieceToVerse(Pdf("Avenant.pdf"), "Avenant", "bis"),
            new PieceToVerse(Pdf("Second.pdf"), "Second avenant", "ter"),
            new PieceToVerse(Pdf("Suite.pdf"), "Suite"),
        ], StartNumber: 68, Stamp: false));

        Assert.Equal(0, result.Failed);
        Assert.Equal(["68", "68 bis", "68 ter", "69"], result.Pieces.Select(piece => piece.Number));
        Assert.True(File.Exists(Pieces("Pièce 68 - Contrat.pdf")));
        Assert.True(File.Exists(Pieces("Pièce 68 bis - Avenant.pdf")));
        Assert.True(File.Exists(Pieces("Pièce 69 - Suite.pdf")));
    }

    /// <summary>
    /// Two pièces cannot share a number, suffix included.
    ///
    /// <para>Two lines of one batch both marked « bis » used to land on 68 bis each, and because
    /// their libellés differ they are two distinct file names: CreateNew sees no collision, both are
    /// written, and « la pièce n° 68 bis » cited in conclusions now points at two documents.</para>
    /// </summary>
    [Fact]
    public async Task RefusesASecondPieceUnderTheSameSuffix()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
        [
            new PieceToVerse(Pdf("Avenant.pdf"), "Avenant", "bis"),
            new PieceToVerse(Pdf("Autre.pdf"), "Autre avenant", "bis"),
        ], StartNumber: 68, Stamp: false));

        Assert.Equal(1, result.Failed);
        Assert.Null(result.Pieces[0].Error);
        Assert.Contains("existe déjà", result.Pieces[1].Error);
        Assert.True(File.Exists(Pieces("Pièce 68 bis - Avenant.pdf")));
        Assert.False(File.Exists(Pieces("Pièce 68 bis - Autre avenant.pdf")));
    }

    /// <summary>And one already on disk from a previous batch is refused the same way.</summary>
    [Fact]
    public async Task RefusesASuffixAlreadyVersed()
    {
        var matter = Dossier();
        Directory.CreateDirectory(Path.Combine(_root, Exhibits.Folder));
        await File.WriteAllTextAsync(Pieces("Pièce 68 bis - Déjà versée.pdf"), "x");

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Encore.pdf"), "Encore", "bis")], StartNumber: 68, Stamp: false));

        Assert.Equal(1, result.Failed);
        Assert.Contains("existe déjà", result.Pieces[0].Error);
    }

    /// <summary>A figure wants a hyphen: « 68 1 » would be two numbers.</summary>
    [Fact]
    public async Task WritesANumberedSuffixWithAHyphen()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Annexe.pdf"), "Annexe", "1")], StartNumber: 68, Stamp: false));

        Assert.Equal("68-1", result.Pieces[0].Number);
        Assert.True(File.Exists(Pieces("Pièce 68-1 - Annexe.pdf")));
    }

    /// <summary>
    /// A dossier versed under the old folder name keeps counting. Reading only the new folder would
    /// restart at 1 and write a second « Pièce 1 » beside one that has been communicated.
    /// </summary>
    [Fact]
    public async Task CountsPastThePiecesVersedUnderTheOldFolderName()
    {
        var matter = Dossier();
        Directory.CreateDirectory(Path.Combine(_root, Exhibits.LegacyFolder));
        await File.WriteAllTextAsync(
            Path.Combine(_root, Exhibits.LegacyFolder, "Pièce 12 - Ancienne.pdf"), "x");

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Nouvelle.pdf"), "Nouvelle")], Stamp: false));

        Assert.Equal("13", result.Pieces[0].Number);

        // Written into Avocado's own folder, and her « Pièces » is left exactly as it was.
        Assert.True(File.Exists(Pieces("Pièce 13 - Nouvelle.pdf")));
        Assert.True(File.Exists(Path.Combine(_root, Exhibits.LegacyFolder, "Pièce 12 - Ancienne.pdf")));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, Exhibits.LegacyFolder)));
    }

    /// <summary>The one that must not lose the afternoon.</summary>
    [Fact]
    public async Task VersesTheRestWhenOneFileIsGone()
    {
        var matter = Dossier();
        var ok = Pdf("Présente.pdf");
        var alsoOk = Pdf("Aussi.pdf");

        var result = await Verse(matter, new VerserPiecesInput(
        [
            new PieceToVerse(ok, "Présente"),
            new PieceToVerse("Disparue.pdf", "Disparue"),
            new PieceToVerse(alsoOk, "Aussi"),
        ], Stamp: false));

        Assert.Equal(1, result.Failed);
        Assert.NotNull(result.Pieces[1].Error);
        Assert.True(File.Exists(Pieces("Pièce 1 - Présente.pdf")));

        // And the number the missing file would have taken is not spent: the third piece is n° 2,
        // so the bordereau has no hole that nothing accounts for.
        Assert.Equal("2", result.Pieces[2].Number);
        Assert.True(File.Exists(Pieces("Pièce 2 - Aussi.pdf")));
    }

    [Fact]
    public async Task RefusesAPathThatClimbsOutOfTheDossier()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse("../../../Windows/System32/notepad.exe", "Rien")], Stamp: false));

        Assert.Equal(1, result.Failed);
        Assert.False(Directory.Exists(Path.Combine(_root, Exhibits.Folder))
            && Directory.GetFiles(Path.Combine(_root, Exhibits.Folder)).Length > 0);
    }

    [Fact]
    public async Task WritesABordereauListingTheLibelles()
    {
        var matter = Dossier();

        await Verse(matter, new VerserPiecesInput(
        [
            new PieceToVerse(Pdf("a.pdf"), "Contrat du 3 mars 2019"),
            new PieceToVerse(Pdf("b.pdf"), "Attestation de M. Loubet"),
        ], Stamp: false));

        var bordereau = Pieces(VerserPieces.BordereauName);
        Assert.True(File.Exists(bordereau));

        var bytes = await File.ReadAllBytesAsync(bordereau);

        // The BOM. Without it Windows opens the file as ANSI and « Pièce » reaches a magistrate as
        // « PiÃ¨ce ».
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        var text = await File.ReadAllTextAsync(bordereau);

        Assert.Contains("BORDEREAU DE COMMUNICATION DE PIÈCES", text);
        Assert.Contains("2026-0001 - Durand contre Martin", text);
        Assert.Contains("N° RG : 23/04567", text);
        Assert.Contains("1. Contrat du 3 mars 2019", text);
        Assert.Contains("2. Attestation de M. Loubet", text);
    }

    [Fact]
    public async Task LeavesTheOriginalsWhereTheyWere()
    {
        var matter = Dossier();
        var name = Pdf("Correspondance/Contrat.pdf");

        await Verse(matter, new VerserPiecesInput([new PieceToVerse(name, "Contrat")], Stamp: false));

        // Versement copies. Moving a document out of the folder she arranged would be Avocado
        // rearranging her dossier for her.
        Assert.True(File.Exists(Path.Combine(_root, "Correspondance", "Contrat.pdf")));
        Assert.True(File.Exists(Pieces("Pièce 1 - Contrat.pdf")));
    }

    [Fact]
    public async Task SaysNothingWasStampedWhenThereIsNoStamp()
    {
        var matter = Dossier();

        // Stamp asked for, but no cabinet image configured. The pièces are still versed and numbered:
        // the stamp is an embellishment, the numbering is the thing she needs.
        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Une.pdf"), "Une")], Stamp: true));

        Assert.Equal(0, result.Failed);
        Assert.False(result.Pieces[0].Stamped);
        Assert.True(File.Exists(Pieces("Pièce 1 - Une.pdf")));
    }

    [Fact]
    public async Task FallsBackToTheFileNameWhenSheWritesNoLibelle()
    {
        var matter = Dossier();

        var result = await Verse(matter, new VerserPiecesInput(
            [new PieceToVerse(Pdf("Attestation Loubet.pdf"), null)], Stamp: false));

        Assert.Equal("Pièce 1 - Attestation Loubet.pdf", result.Pieces[0].Name);
    }

    public void Dispose()
    {
        _vault.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// These tests configure no stamp, so the batch must never reach for a vault at all. Throwing
    /// here is the assertion: if versement ever starts resolving the coffre when there is nothing to
    /// read from it, every one of these tests fails rather than quietly gaining a dependency.
    /// </summary>
    private sealed class NoVault : Avocado.Vault.IVaultStore
    {
        public Avocado.Vault.OpenVault Get(Guid id) =>
            throw new InvalidOperationException("Versement resolved the coffre with no stamp configured.");

        public bool TryGet(Guid id, out Avocado.Vault.OpenVault? vault)
        {
            vault = null;
            return false;
        }
    }
}
