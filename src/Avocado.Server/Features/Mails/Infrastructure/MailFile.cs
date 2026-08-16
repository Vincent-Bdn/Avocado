using MimeKit;

namespace Avocado.Server.Features.Mails.Infrastructure;

/// <summary>
/// Reads an email off disk, whether Outlook wrote it or a MIME client did.
///
/// <para>Two formats because two things produce them. Dragging a message out of Outlook materialises
/// a <c>.msg</c>, which is an OLE compound document; everything else, and every export from a webmail,
/// produces a <c>.eml</c>, which is MIME. Both are read here and both come out as
/// <see cref="ParsedMail"/>, so nothing downstream has to care.</para>
///
/// <para>Both readers are pure managed, which is not an accident: Avocado publishes self-contained
/// single-file for six runtime identifiers, and a native dependency is six more things that can fail
/// to load on the one machine nobody tested.</para>
/// </summary>
public static class MailFile
{
    public static readonly string[] Extensions = [".eml", ".msg"];

    public static bool LooksLikeMail(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the file. Throws <see cref="MailFormatException"/> rather than whatever the underlying
    /// reader felt like, since the caller is a folder watcher that has to keep going.
    /// </summary>
    public static ParsedMail Read(string path)
    {
        try
        {
            return Path.GetExtension(path).Equals(".msg", StringComparison.OrdinalIgnoreCase)
                ? ReadOutlook(path)
                : ReadMime(path);
        }
        catch (Exception exception) when (exception is not MailFormatException)
        {
            throw new MailFormatException(
                $"« {Path.GetFileName(path)} » n'a pas pu être lu comme un message.", exception);
        }
    }

    private static ParsedMail ReadMime(string path)
    {
        using var message = MimeMessage.Load(path);

        var attachments = new List<MailAttachment>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            if (part.Content is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            part.Content.DecodeTo(buffer);

            attachments.Add(new MailAttachment(
                SafeName(part.FileName),
                part.ContentType?.MimeType,
                buffer.ToArray()));
        }

        return new ParsedMail(
            message.Subject ?? string.Empty,
            message.Date,
            Convert(message.From.Mailboxes.FirstOrDefault()),
            message.To.Mailboxes.Select(Convert).OfType<MailAddress>().ToList(),
            message.Cc.Mailboxes.Select(Convert).OfType<MailAddress>().ToList(),
            message.TextBody ?? StripHtml(message.HtmlBody),
            attachments);
    }

    private static ParsedMail ReadOutlook(string path)
    {
        using var stream = File.OpenRead(path);
        using var message = new MsgReader.Outlook.Storage.Message(stream);

        var attachments = new List<MailAttachment>();
        foreach (var attachment in message.Attachments.OfType<MsgReader.Outlook.Storage.Attachment>())
        {
            // An inline image belongs to the body, not to the dossier. Filing a signature logo as a
            // pièce is how a matter ends up with forty copies of a letterhead.
            if (attachment.IsInline)
            {
                continue;
            }

            attachments.Add(new MailAttachment(
                SafeName(attachment.FileName),
                null,
                attachment.Data));
        }

        var sender = message.Sender is { } from && !string.IsNullOrWhiteSpace(from.Email)
            ? new MailAddress(from.Email.Trim().ToLowerInvariant(), from.DisplayName)
            : (MailAddress?)null;

        return new ParsedMail(
            message.Subject ?? string.Empty,
            // SentOn is absent on a draft, and on some Gestisoft exports. Received is the next best
            // truth, and the file's own timestamp is the last resort: a mail with no date at all
            // sorts to 1601 and disappears from the top of a journal nobody then trusts.
            message.SentOn ?? message.ReceivedOn ?? File.GetLastWriteTimeUtc(path),
            sender,
            Recipients(message, MsgReader.Outlook.RecipientType.To),
            Recipients(message, MsgReader.Outlook.RecipientType.Cc),
            message.BodyText ?? StripHtml(message.BodyHtml),
            attachments);
    }

    private static List<MailAddress> Recipients(
        MsgReader.Outlook.Storage.Message message,
        MsgReader.Outlook.RecipientType type) =>
        message.Recipients
            .Where(recipient => recipient.Type == type && !string.IsNullOrWhiteSpace(recipient.Email))
            .Select(recipient => new MailAddress(recipient.Email!.Trim().ToLowerInvariant(), recipient.DisplayName))
            .ToList();

    private static MailAddress? Convert(MailboxAddress? mailbox) =>
        mailbox is null || string.IsNullOrWhiteSpace(mailbox.Address)
            ? null
            : new MailAddress(mailbox.Address.Trim().ToLowerInvariant(), mailbox.Name);

    /// <summary>
    /// A file name out of a message is attacker-controlled in the ordinary case where the sender is
    /// not the user. Anything resembling a path is reduced to its last segment, so a helpful
    /// « ../../vault.json » stays an ordinary file name.
    /// </summary>
    private static string SafeName(string? name)
    {
        var candidate = Path.GetFileName((name ?? string.Empty).Replace('\\', '/').Trim());

        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or "..")
        {
            return "piece-jointe";
        }

        return string.Concat(candidate.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
    }

    /// <summary>
    /// Enough to make an HTML-only mail searchable and readable in the journal. Not a renderer: the
    /// original is kept as a document and opens in whatever the machine uses for mail.
    /// </summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder(html.Length);
        var inside = false;

        foreach (var character in html)
        {
            if (character == '<') inside = true;
            else if (character == '>') inside = false;
            else if (!inside) text.Append(character);
        }

        return System.Net.WebUtility.HtmlDecode(text.ToString()).Trim();
    }
}

public sealed class MailFormatException(string message, Exception innerException)
    : Exception(message, innerException);
