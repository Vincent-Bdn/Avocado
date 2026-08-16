using Avocado.Server.Features.Mails;

namespace Avocado.Server.Tests.Mails;

/// <summary>
/// The rule that decides where a dropped email lands. Dropping thirty messages is only an improvement
/// if thirty decisions do not come back out, so this has to be right often enough to be trusted, and
/// modest enough to be believed when it says it is unsure.
/// </summary>
public class MailFilingTests
{
    private static readonly Guid Dupont = Guid.NewGuid();
    private static readonly Guid Martin = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    private static ParsedMail Mail(params string[] recipients) =>
        new(
            "Conclusions",
            DateTimeOffset.UtcNow,
            new MailAddress("client@exemple.fr", "SARL Dupont"),
            recipients.Select(address => new MailAddress(address, null)).ToList(),
            [],
            "Bonjour Maître,",
            []);

    [Fact]
    public void FilesAMessageWhoseParticipantsPointAtOneOpenDossier()
    {
        var decision = MailFiling.Decide(
            Mail("avocat@cabinet.fr"),
            [new MailCandidate(Dupont, Client, "client@exemple.fr", IsOpen: true)]);

        Assert.Equal(MailVerdict.Filed, decision.Verdict);
        Assert.Equal(Dupont, decision.MatterId);
        Assert.Contains("client@exemple.fr", decision.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mail she sent belongs in the same dossier as one she received, and there the client is a
    /// recipient rather than the sender. Looking only at From would file half a conversation.
    /// </summary>
    [Fact]
    public void MatchesOnRecipientsAndNotOnlyOnTheSender()
    {
        var outgoing = new ParsedMail(
            "RE: Conclusions",
            DateTimeOffset.UtcNow,
            new MailAddress("avocat@cabinet.fr", "Maître"),
            [new MailAddress("client@exemple.fr", null)],
            [],
            string.Empty,
            []);

        Assert.Contains(outgoing.Participants, participant => participant.Address == "client@exemple.fr");
        Assert.Contains(outgoing.Participants, participant => participant.Address == "avocat@cabinet.fr");
    }

    /// <summary>
    /// A long-standing client is party to several matters, most of them finished. A new message is
    /// overwhelmingly about the live one, so a closed dossier never competes with an open one.
    /// </summary>
    [Fact]
    public void AnOpenDossierBeatsAClosedOne()
    {
        var decision = MailFiling.Decide(
            Mail("avocat@cabinet.fr"),
            [
                new MailCandidate(Dupont, Client, "client@exemple.fr", IsOpen: false),
                new MailCandidate(Martin, Client, "client@exemple.fr", IsOpen: true),
            ]);

        Assert.Equal(MailVerdict.Filed, decision.Verdict);
        Assert.Equal(Martin, decision.MatterId);
    }

    /// <summary>Only closed dossiers is still an answer, and saying so beats pretending nobody matched.</summary>
    [Fact]
    public void FilesIntoAClosedDossierWhenThatIsTheOnlyMatch()
    {
        var decision = MailFiling.Decide(
            Mail("avocat@cabinet.fr"),
            [new MailCandidate(Dupont, Client, "client@exemple.fr", IsOpen: false)]);

        Assert.Equal(MailVerdict.Filed, decision.Verdict);
        Assert.Equal(Dupont, decision.MatterId);
        Assert.Contains("clôturé", decision.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two live matters with the same client is an ordinary situation, and guessing puts
    /// correspondence in the wrong dossier, which is worse than asking.
    /// </summary>
    [Fact]
    public void RefusesToGuessBetweenTwoOpenDossiers()
    {
        var decision = MailFiling.Decide(
            Mail("avocat@cabinet.fr"),
            [
                new MailCandidate(Dupont, Client, "client@exemple.fr", IsOpen: true),
                new MailCandidate(Martin, Client, "client@exemple.fr", IsOpen: true),
            ]);

        Assert.Equal(MailVerdict.Ambiguous, decision.Verdict);
        Assert.Null(decision.MatterId);
        Assert.Equal(2, decision.Candidates.Count);
    }

    /// <summary>
    /// Several people on the message who are all party to the same dossier is not ambiguity: it is
    /// the same answer arrived at twice, and asking would be noise.
    /// </summary>
    [Fact]
    public void SeveralParticipantsInOneDossierIsNotAmbiguous()
    {
        var decision = MailFiling.Decide(
            Mail("confrere@exemple.fr"),
            [
                new MailCandidate(Dupont, Client, "client@exemple.fr", IsOpen: true),
                new MailCandidate(Dupont, Guid.NewGuid(), "confrere@exemple.fr", IsOpen: true),
            ]);

        Assert.Equal(MailVerdict.Filed, decision.Verdict);
        Assert.Equal(Dupont, decision.MatterId);
    }

    [Fact]
    public void LeavesAMessageFromNobodyKnownInTheTray()
    {
        var decision = MailFiling.Decide(Mail("inconnu@exemple.fr"), []);

        Assert.Equal(MailVerdict.Unknown, decision.Verdict);
        Assert.Null(decision.MatterId);
        Assert.Empty(decision.Candidates);
    }

    [Fact]
    public void EveryDecisionCarriesAReasonToShow()
    {
        foreach (var decision in new[]
                 {
                     MailFiling.Decide(Mail("a@b.fr"), []),
                     MailFiling.Decide(Mail("a@b.fr"), [new MailCandidate(Dupont, Client, "client@exemple.fr", true)]),
                     MailFiling.Decide(Mail("a@b.fr"),
                     [
                         new MailCandidate(Dupont, Client, "client@exemple.fr", true),
                         new MailCandidate(Martin, Client, "client@exemple.fr", true),
                     ]),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        }
    }

    [Fact]
    public void AMessageWithNoSubjectStillHasSomethingToShowInTheJournal()
    {
        var blank = Mail("a@b.fr") with { Subject = "   " };

        Assert.Equal("(sans objet)", blank.DisplayTitle);
    }
}
