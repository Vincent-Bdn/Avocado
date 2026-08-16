namespace Avocado.Server.Features.Mails;

/// <param name="Address">Lower-cased, so matching never turns on how a client capitalised it.</param>
/// <param name="Name">The display name, when the message carried one.</param>
public readonly record struct MailAddress(string Address, string? Name)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Address : $"{Name} <{Address}>";
}

/// <param name="FileName">As it was named in the message, already stripped of any path.</param>
public sealed record MailAttachment(string FileName, string? ContentType, byte[] Content);

/// <summary>
/// One email, read out of a <c>.eml</c> or a <c>.msg</c> and reduced to what a dossier cares about.
///
/// <para>Deliberately the same shape whichever format it came from. Outlook's <c>.msg</c> and MIME are
/// very different containers, and every part of Avocado downstream of this, the filing rule, the
/// journal entry, the bulk import, would otherwise have to know which one it was looking at.</para>
/// </summary>
/// <param name="Participants">
/// Everyone on the message, sender and recipients together. Filing looks at all of them, because a
/// mail she sent to a client belongs in the same dossier as one she received from them, and the
/// client is the recipient in the first case.
/// </param>
public sealed record ParsedMail(
    string Subject,
    DateTimeOffset SentAt,
    MailAddress? From,
    IReadOnlyList<MailAddress> To,
    IReadOnlyList<MailAddress> Cc,
    string BodyText,
    IReadOnlyList<MailAttachment> Attachments)
{
    public IEnumerable<MailAddress> Participants =>
        (From is { } from ? [from] : Array.Empty<MailAddress>()).Concat(To).Concat(Cc);

    /// <summary>
    /// What the journal entry is titled. A message with no subject is common enough that falling back
    /// to « (sans objet) » is better than an empty line in the timeline.
    /// </summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Subject) ? "(sans objet)" : Subject.Trim();
}
