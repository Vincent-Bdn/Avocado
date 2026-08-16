using Avocado.Server.Features.Documents.Checkout;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// What happens on the launch after a crash, which is the only launch where any of this matters.
/// </summary>
public class CheckoutResumptionTests
{
    private static readonly Guid Assignation = Guid.NewGuid();

    private static readonly BorrowedFile[] Handed =
    [
        new(Assignation, "assignation.pdf", "aaa", 1000),
        new(Guid.NewGuid(), "conclusions.docx", "bbb", 2000),
    ];

    [Fact]
    public void AFolderThatIsGoneMeansTheReturnFinished()
    {
        var resumption = CheckoutResumptionCheck.Assess(folderExists: false, Handed, []);

        Assert.Equal(ResumeVerdict.Completed, resumption.Verdict);
        Assert.False(resumption.NeedsAsking);
    }

    /// <summary>
    /// The ordinary crash: the folder survived exactly as it was handed over. There is nothing to
    /// recover and nothing to decide, so the dossier reopens without a word. Announcing a state that
    /// costs someone nothing is how warnings stop being read.
    /// </summary>
    [Fact]
    public void AnUntouchedFolderReopensSilently()
    {
        var resumption = CheckoutResumptionCheck.Assess(
            folderExists: true,
            Handed,
            [new FolderFile("assignation.pdf", "aaa", 1000), new FolderFile("conclusions.docx", "bbb", 2000)]);

        Assert.Equal(ResumeVerdict.Intact, resumption.Verdict);
        Assert.False(resumption.NeedsAsking);
        Assert.Empty(resumption.Changes);
    }

    /// <summary>
    /// Work done while Avocado was not running. Not a fault, and not something to resolve on her
    /// behalf: the folder is the only copy of it, and the application deciding would be the
    /// application destroying an afternoon to tidy up after itself.
    /// </summary>
    [Fact]
    public void AnEditedFolderIsBroughtBackToHer()
    {
        var resumption = CheckoutResumptionCheck.Assess(
            folderExists: true,
            Handed,
            [new FolderFile("assignation.pdf", "aaa", 1000), new FolderFile("conclusions.docx", "ccc", 2400)]);

        Assert.Equal(ResumeVerdict.Changed, resumption.Verdict);
        Assert.True(resumption.NeedsAsking);
        Assert.Equal(CheckoutChangeKind.Modified, Assert.Single(resumption.Changes).Kind);
    }

    [Fact]
    public void AFileAddedWhileAvocadoWasOffIsOffered()
    {
        var resumption = CheckoutResumptionCheck.Assess(
            folderExists: true,
            [],
            [new FolderFile("courriel.msg", "ddd", 800)]);

        Assert.Equal(ResumeVerdict.Changed, resumption.Verdict);
        Assert.Equal(CheckoutChangeKind.Added, Assert.Single(resumption.Changes).Kind);
    }

    /// <summary>
    /// Word's lock file is the single most likely difference to find after a crash, since a crash is
    /// exactly when Word does not get to clean up. Treating it as a change would make the prompt
    /// appear every time and mean nothing.
    /// </summary>
    [Fact]
    public void WordsLeftoverLockFileIsNotAChange()
    {
        var resumption = CheckoutResumptionCheck.Assess(
            folderExists: true,
            Handed,
            [
                new FolderFile("assignation.pdf", "aaa", 1000),
                new FolderFile("conclusions.docx", "bbb", 2000),
                new FolderFile("~$conclusions.docx", "lock", 162),
            ]);

        Assert.Equal(ResumeVerdict.Intact, resumption.Verdict);
    }

    /// <summary>
    /// An emptied folder is still only a question. Deletion belongs to a return she asked for, and
    /// never to a startup check acting on its own reading of a directory.
    /// </summary>
    [Fact]
    public void AnEmptiedFolderIsAskedAboutRatherThanActedOn()
    {
        var resumption = CheckoutResumptionCheck.Assess(folderExists: true, Handed, []);

        Assert.Equal(ResumeVerdict.Changed, resumption.Verdict);
        Assert.Equal(2, resumption.Changes.Count);
        Assert.All(resumption.Changes, change => Assert.Equal(CheckoutChangeKind.Deleted, change.Kind));
    }
}
