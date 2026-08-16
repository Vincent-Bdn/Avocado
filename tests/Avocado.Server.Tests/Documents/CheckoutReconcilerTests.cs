using Avocado.Server.Features.Documents.Checkout;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// What happened to a dossier's folder while it was borrowed. Decrypting files is the easy half;
/// deciding what it means when one is no longer where it was is the half where every wrong answer is
/// somebody's pièce.
/// </summary>
public class CheckoutReconcilerTests
{
    private static readonly Guid Assignation = Guid.NewGuid();
    private static readonly Guid Conclusions = Guid.NewGuid();

    private static BorrowedFile Borrowed(Guid id, string path, string hash, long size = 1000) =>
        new(id, path, hash, size);

    private static FolderFile Present(string path, string hash, long size = 1000) =>
        new(path, hash, size);

    [Fact]
    public void AnUntouchedFolderReportsNothingNotable()
    {
        var changes = CheckoutReconciler.Compare(
            [Borrowed(Assignation, "assignation.pdf", "aaa")],
            [Present("assignation.pdf", "aaa")]);

        Assert.Equal(CheckoutChangeKind.Unchanged, Assert.Single(changes).Kind);
        Assert.Empty(CheckoutReconciler.Notable(changes));
    }

    [Fact]
    public void AnEditedFileIsModifiedAndKeepsItsDocument()
    {
        var change = Assert.Single(CheckoutReconciler.Compare(
            [Borrowed(Conclusions, "conclusions.docx", "aaa")],
            [Present("conclusions.docx", "bbb", 2000)]));

        Assert.Equal(CheckoutChangeKind.Modified, change.Kind);
        Assert.Equal(Conclusions, change.DocumentId);
        Assert.Equal("bbb", change.Sha256);
        Assert.Equal(2000, change.SizeBytes);
    }

    [Fact]
    public void AFileDroppedInIsAdded()
    {
        var change = Assert.Single(CheckoutReconciler.Compare(
            [],
            [Present("courriel du client.msg", "ccc")]));

        Assert.Equal(CheckoutChangeKind.Added, change.Kind);
        Assert.Null(change.DocumentId);
    }

    [Fact]
    public void AFileTakenAwayIsDeleted()
    {
        var change = Assert.Single(CheckoutReconciler.Compare(
            [Borrowed(Assignation, "assignation.pdf", "aaa")],
            []));

        Assert.Equal(CheckoutChangeKind.Deleted, change.Kind);
        Assert.Equal(Assignation, change.DocumentId);
    }

    /// <summary>
    /// The rule that earns its keep. Renaming in Explorer is ordinary, and treating it as a delete
    /// plus an add would silently drop the document's classification, its pièce number and its place
    /// in the journal, which is most of what Avocado knows about it.
    /// </summary>
    [Fact]
    public void RenamingAFileKeepsTheDocumentItCameFrom()
    {
        var change = Assert.Single(CheckoutReconciler.Compare(
            [Borrowed(Assignation, "scan001.pdf", "aaa")],
            [Present("assignation du 14 août.pdf", "aaa")]));

        Assert.Equal(CheckoutChangeKind.Renamed, change.Kind);
        Assert.Equal(Assignation, change.DocumentId);
        Assert.Equal("scan001.pdf", change.PreviousPath);
        Assert.Equal("assignation du 14 août.pdf", change.RelativePath);
    }

    /// <summary>
    /// A rename and an edit at once is not a rename: the contents no longer match, so there is nothing
    /// to recognise it by. Reported as a delete and an add, which is the honest answer rather than a
    /// guess that could attach a document to the wrong file.
    /// </summary>
    [Fact]
    public void ARenamedAndEditedFileIsNotClaimedAsARename()
    {
        var changes = CheckoutReconciler.Compare(
            [Borrowed(Assignation, "scan001.pdf", "aaa")],
            [Present("assignation.pdf", "bbb")]);

        Assert.Contains(changes, change => change.Kind == CheckoutChangeKind.Deleted);
        Assert.Contains(changes, change => change.Kind == CheckoutChangeKind.Added);
        Assert.DoesNotContain(changes, change => change.Kind == CheckoutChangeKind.Renamed);
    }

    /// <summary>
    /// Two identical files is a real situation, a scan filed twice. One rename must not consume both
    /// origins, or a document would be attached to a file that never came from it.
    /// </summary>
    [Fact]
    public void TwoIdenticalFilesDoNotBothClaimTheSameOrigin()
    {
        var changes = CheckoutReconciler.Compare(
            [Borrowed(Assignation, "a.pdf", "aaa"), Borrowed(Conclusions, "b.pdf", "aaa")],
            [Present("renommé.pdf", "aaa")]);

        Assert.Single(changes, change => change.Kind == CheckoutChangeKind.Renamed);
        Assert.Single(changes, change => change.Kind == CheckoutChangeKind.Deleted);
    }

    /// <summary>
    /// Word writes a ~$ lock file beside every open document. Offering to file it as a pièce would
    /// train someone to click through the review without reading it, which defeats the review.
    /// </summary>
    [Theory]
    [InlineData("~$conclusions.docx")]
    [InlineData("Thumbs.db")]
    [InlineData(".DS_Store")]
    [InlineData("desktop.ini")]
    [InlineData("assignation.pdf.tmp")]
    [InlineData("piece.pdf.crdownload")]
    [InlineData(".~lock.conclusions.odt#")]
    public void IgnoresWhatEditorsAndFileManagersLeaveBehind(string name)
    {
        Assert.True(CheckoutReconciler.IsDebris(name));
        Assert.Empty(CheckoutReconciler.Compare([], [Present(name, "zzz")]));
    }

    [Fact]
    public void DoesNotMistakeARealDocumentForDebris()
    {
        Assert.False(CheckoutReconciler.IsDebris("assignation.pdf"));
        Assert.False(CheckoutReconciler.IsDebris("note~1.docx"));
    }

    /// <summary>A dossier worked on for an afternoon, with one of everything.</summary>
    [Fact]
    public void HandlesAnAfternoonsWorkAtOnce()
    {
        var pieceTrois = Guid.NewGuid();
        var vieux = Guid.NewGuid();

        var changes = CheckoutReconciler.Compare(
            [
                Borrowed(Assignation, "assignation.pdf", "aaa"),
                Borrowed(Conclusions, "conclusions.docx", "bbb"),
                Borrowed(pieceTrois, "scan003.pdf", "ccc"),
                Borrowed(vieux, "brouillon.docx", "ddd"),
            ],
            [
                Present("assignation.pdf", "aaa"),
                Present("conclusions.docx", "bbb2"),
                Present("pièce n°3, constat.pdf", "ccc"),
                Present("courriel du client.msg", "eee"),
                Present("~$conclusions.docx", "lock"),
            ]);

        var notable = CheckoutReconciler.Notable(changes);

        Assert.Equal(4, notable.Count);
        Assert.Single(notable, change => change.Kind == CheckoutChangeKind.Modified && change.DocumentId == Conclusions);
        Assert.Single(notable, change => change.Kind == CheckoutChangeKind.Renamed && change.DocumentId == pieceTrois);
        Assert.Single(notable, change => change.Kind == CheckoutChangeKind.Deleted && change.DocumentId == vieux);
        Assert.Single(notable, change => change.Kind == CheckoutChangeKind.Added);
    }
}
