namespace Avocado.Server.Features.Mails;

/// <param name="MatterId">The dossier this address points at.</param>
/// <param name="ContactId">Who the address belonged to, so the reason can be shown.</param>
public readonly record struct MailCandidate(Guid MatterId, Guid ContactId, string Address, bool IsOpen);

public enum MailVerdict
{
    /// <summary>One open dossier, and it is filed there.</summary>
    Filed,

    /// <summary>Several dossiers claim it. She decides, and the candidates are offered.</summary>
    Ambiguous,

    /// <summary>Nobody on the message is in the carnet. It waits in the tray.</summary>
    Unknown,
}

/// <param name="Reason">In French, for the line under the journal entry saying why it landed here.</param>
public sealed record MailFilingDecision(
    MailVerdict Verdict,
    Guid? MatterId,
    string Reason,
    IReadOnlyList<MailCandidate> Candidates);

/// <summary>
/// Decides which dossier an email belongs to.
///
/// <para>The transport is the boring half of this feature. Dropping thirty messages into a folder is
/// only an improvement if thirty decisions do not come back out, so the rule has to be right often
/// enough to be trusted and modest enough to be believed when it is unsure.</para>
///
/// <para><b>It looks at everyone on the message, not just the sender.</b> A mail she sent to a client
/// belongs in the same dossier as one she received from them, and in the first case the client is a
/// recipient. Anything else would file half a conversation.</para>
///
/// <para><b>Closed dossiers lose to open ones.</b> A client who has been with the practice for years
/// is party to several matters, most of them finished, and a new message is overwhelmingly about the
/// live one. When only closed dossiers match, that is still an answer worth offering rather than
/// pretending nobody was recognised.</para>
///
/// <para>Ambiguity is never resolved by guessing. Two open dossiers with the same client is a real
/// situation and the wrong choice puts correspondence in the wrong matter, which is worse than asking.</para>
/// </summary>
public static class MailFiling
{
    public static MailFilingDecision Decide(ParsedMail mail, IReadOnlyList<MailCandidate> matches)
    {
        if (matches.Count == 0)
        {
            return new MailFilingDecision(
                MailVerdict.Unknown,
                null,
                "Aucun destinataire de ce message ne figure dans le carnet.",
                []);
        }

        var open = matches.Where(candidate => candidate.IsOpen).ToList();
        var considered = open.Count > 0 ? open : matches;

        var dossiers = considered.Select(candidate => candidate.MatterId).Distinct().ToList();

        if (dossiers.Count == 1)
        {
            var winner = considered.First();

            return new MailFilingDecision(
                MailVerdict.Filed,
                winner.MatterId,
                open.Count > 0
                    ? $"Classé automatiquement : {winner.Address} est au dossier."
                    : $"Classé automatiquement : {winner.Address} est à ce dossier, clôturé.",
                considered);
        }

        return new MailFilingDecision(
            MailVerdict.Ambiguous,
            null,
            $"{dossiers.Count} dossiers correspondent aux participants de ce message.",
            considered);
    }
}
