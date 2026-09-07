using Avocado.Server.Features.Documents.Folders;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// Reading the folder a dossier lives in, which is now her own folder rather than a copy Avocado
/// keeps. Ten lawyers refused the copy, so what is on disk is the truth and this is what reads it.
/// </summary>
public class DossierFolderReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"avocado-folder-{Guid.NewGuid():N}");

    public DossierFolderReaderTests() => Directory.CreateDirectory(_root);

    private void File_(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
    }

    private FolderListing Read(string? relative = null) =>
        DossierFolderReader.Read(_root, relative, default);

    [Fact]
    public void ListsWhatIsInTheFolder()
    {
        File_("01 Courriers/lettre.pdf");
        File_("assignation.pdf");

        var listing = Read();

        Assert.False(listing.Missing);
        Assert.Equal(2, listing.Files);
        Assert.Collection(
            listing.Entries,
            entry => Assert.Equal(("01 Courriers", true), (entry.Name, entry.IsFolder)),
            entry => Assert.Equal(("assignation.pdf", false), (entry.Name, entry.IsFolder)));
    }

    /// <summary>Folders before files, and « 2 » before « 10 », the way a file manager orders them.</summary>
    [Fact]
    public void OrdersFoldersFirstAndNumbersNumerically()
    {
        File_("10 Pièces/a.pdf");
        File_("2 Actes/b.pdf");
        File_("z.pdf");
        File_("a.pdf");

        Assert.Equal(
            ["2 Actes", "10 Pièces", "a.pdf", "z.pdf"],
            Read().Entries.Select(entry => entry.Name).ToList());
    }

    [Fact]
    public void DescendsIntoASubfolder()
    {
        File_("01 Courriers/lettre.pdf");

        var listing = Read("01 Courriers");

        Assert.Equal("lettre.pdf", Assert.Single(listing.Entries).Name);
    }

    /// <summary>
    /// The relative path arrives from the window, and the window is not to be trusted with it: this
    /// endpoint shows one dossier, and « ../.. » would show her home directory.
    /// </summary>
    [Theory]
    [InlineData("../..")]
    [InlineData("01 Courriers/../../..")]
    [InlineData("/")]
    public void RefusesToClimbOutOfTheFolder(string climb)
    {
        File_("dedans.pdf");

        // Falls back to the root rather than erroring: she asked for a folder that is not hers to see,
        // and the honest answer is the one she is entitled to.
        Assert.Equal("dedans.pdf", Assert.Single(Read(climb).Entries).Name);
    }

    [Fact]
    public void SaysSoWhenTheFolderIsGone()
    {
        var listing = DossierFolderReader.Read(Path.Combine(_root, "parti"), null, default);

        Assert.True(listing.Missing);
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public void HasNoPathWhenTheDossierHasNoFolder()
    {
        var listing = DossierFolderReader.Read(null, null, default);

        Assert.Null(listing.Path);
        Assert.False(listing.Missing);
    }

    /// <summary>Courriels are what half of a dossier is, and the list marks them.</summary>
    [Fact]
    public void MarksTheMessages()
    {
        File_("échange.msg");
        File_("note.pdf");

        var listing = Read();

        Assert.True(Assert.Single(listing.Entries, entry => entry.Name == "échange.msg").IsMail);
        Assert.False(Assert.Single(listing.Entries, entry => entry.Name == "note.pdf").IsMail);
    }

    [Fact]
    public void LeavesOutWhatWindowsPutThere()
    {
        File_("Thumbs.db");
        File_("~$conclusions.docx");
        File_("conclusions.docx");

        Assert.Equal("conclusions.docx", Assert.Single(Read().Entries).Name);
    }

    /// <summary>A folder carries the weight of everything under it, so a row can say what it holds.</summary>
    [Fact]
    public void CountsEverythingUnderneathForTheTotal()
    {
        File_("01 Courriers/a.pdf", "aaaa");
        File_("01 Courriers/02 Annexes/b.pdf", "bb");

        Assert.Equal(2, Read().Files);
        Assert.Equal(6, Assert.Single(Read().Entries).SizeBytes);
    }

    [Fact]
    public void MarksThePiecesInTheListing()
    {
        File_("Pièces/Pièce 4 - Attestation.pdf");

        Assert.Equal(4, Assert.Single(Read("Pièces").Entries).ExhibitNumber);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
