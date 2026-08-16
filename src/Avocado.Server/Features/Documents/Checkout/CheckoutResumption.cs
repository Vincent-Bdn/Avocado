namespace Avocado.Server.Features.Documents.Checkout;

public enum ResumeVerdict
{
    /// <summary>The folder is gone, so the return completed. Nothing to do and nothing to say.</summary>
    Completed,

    /// <summary>Still there and byte for byte as we left it. Reopen the dossier and stay quiet.</summary>
    Intact,

    /// <summary>Still there and different. Somebody worked while Avocado was not running, and is asked.</summary>
    Changed,
}

/// <param name="Changes">Empty unless <see cref="ResumeVerdict.Changed"/>. What to show her.</param>
public sealed record CheckoutResumption(ResumeVerdict Verdict, IReadOnlyList<CheckoutChange> Changes)
{
    public bool NeedsAsking => Verdict is ResumeVerdict.Changed;
}

/// <summary>
/// What to do at startup about a dossier folder that is still on disk.
///
/// <para>Avocado deletes these folders on the way out, and sometimes it does not get the chance: a
/// crash, a power cut, a file still held open by Word. The obvious reaction is to clean up on the next
/// launch, and it is the wrong one. The folder may hold an afternoon nobody saved, and deleting it
/// would be the application destroying work to tidy up after itself.</para>
///
/// <para>So the folder is compared to what we recorded when it was handed over. Identical means the
/// return simply never ran, there is nothing to recover, and the dossier reopens without a word:
/// telling someone about a state that costs them nothing is how warnings stop being read. Different
/// means somebody worked on it while Avocado was off, which is not a fault and not something to
/// resolve on their behalf, so it is shown and she decides.</para>
///
/// <para>Nothing is ever deleted on the strength of this. Deletion belongs to a return she asked for.</para>
/// </summary>
public static class CheckoutResumptionCheck
{
    public static CheckoutResumption Assess(
        bool folderExists,
        IReadOnlyList<BorrowedFile> borrowed,
        IReadOnlyList<FolderFile> present)
    {
        if (!folderExists)
        {
            return new CheckoutResumption(ResumeVerdict.Completed, []);
        }

        var notable = CheckoutReconciler.Notable(CheckoutReconciler.Compare(borrowed, present));

        return notable.Count == 0
            ? new CheckoutResumption(ResumeVerdict.Intact, [])
            : new CheckoutResumption(ResumeVerdict.Changed, notable);
    }
}
