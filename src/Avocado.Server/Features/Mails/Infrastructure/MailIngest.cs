using Avocado.Server.Data;
using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Documents;
using Avocado.Server.Features.Settings.Infrastructure;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Mails.Infrastructure;

/// <summary>
/// Turns an email dropped into a dossier folder into what it actually is: an entry in the journal,
/// with its attachments filed as pièces.
///
/// <para>Without this a .msg is stored as an opaque document called « RE  votre dossier.msg », which
/// is where it stops being useful. The journal is where a practice reconstructs what happened and
/// when, and a message that arrived is exactly that kind of fact. The client's PDF being findable
/// beside it, rather than sealed inside a container only Outlook opens, is the other half.</para>
///
/// <para>Dragging a message out of Outlook into an open dossier folder is therefore the whole email
/// feature. No server, no per-dossier address, no add-in, no credentials: a gesture people already
/// know, into a folder that already exists.</para>
/// </summary>
public sealed class MailIngest(ILogger<MailIngest> logger)
{
    /// <summary>
    /// Records the message against <paramref name="matterId"/> and returns its attachments so the
    /// caller can store them as documents of their own. Returns null when the file is not a message,
    /// or cannot be read as one, in which case it stays an ordinary document.
    /// </summary>
    /// <param name="ownAddresses">
    /// The practice's own addresses, from Réglages, read once by the caller. Given none, every message
    /// is filed as reçu, which is what happened to all 4 713 of them before this existed.
    /// </param>
    public async Task<IReadOnlyList<MailAttachment>?> RecordAsync(
        AvocadoDbContext database,
        Guid matterId,
        Guid documentId,
        string path,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? ownAddresses = null)
    {
        if (!MailFile.LooksLikeMail(path))
        {
            return null;
        }

        ParsedMail mail;

        try
        {
            mail = MailFile.Read(path);
        }
        catch (MailFormatException exception)
        {
            // A file named .msg that is not one, or a format this version does not understand. It is
            // still a document in the dossier, which is the important part.
            logger.LogWarning(exception, "Kept {File} as a plain document.", Path.GetFileName(path));
            return null;
        }

        var addresses = mail.Participants.Select(participant => participant.Address).Distinct().ToList();

        // Whoever on the message is already in the carnet. Used only to attribute the entry: the
        // dossier is not in question here, she dropped it into that folder herself.
        var contact = await database.Contacts
            .Where(candidate => candidate.Email != null && addresses.Contains(candidate.Email.ToLower()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var activity = new Activity
        {
            MatterId = matterId,
            OccurredAt = mail.SentAt,
            Type = Direction(mail, ownAddresses),
            ContactId = contact?.Id,
            Subject = mail.DisplayTitle,
            Body = Excerpt(mail.BodyText),
        };

        database.Activities.Add(activity);

        // The message itself hangs off the entry, so opening the journal line opens the mail.
        var document = await database.Documents.FindAsync([documentId], cancellationToken).ConfigureAwait(false);
        if (document is not null)
        {
            document.ActivityId = activity.Id;
            document.Type = "Courriel";
            document.DocumentDate = DateOnly.FromDateTime(mail.SentAt.UtcDateTime);
        }

        logger.LogInformation(
            "Filed « {Subject} » as a journal entry with {Count} attachment(s).",
            activity.Subject, mail.Attachments.Count);

        return mail.Attachments;
    }

    /// <summary>
    /// Enough of the body to recognise the message in a timeline. The whole thing is in the document,
    /// and a journal that renders three screens of quoted history is a journal nobody scrolls.
    /// </summary>
    /// <summary>
    /// Reçu or envoyé, which is decided by whether the sender is her.
    ///
    /// <para>Nothing in the vault used to record who she was, so every message was filed as reçu:
    /// consistently the common case, correctable in a click, and wrong for every message she had ever
    /// sent. An earlier attempt inferred it from whether the sender was in the carnet, which answers a
    /// different question and gave the same answer for every message anyway.</para>
    ///
    /// <para>Still reçu when she has told Avocado nothing, and when the message carries no sender at
    /// all, which happens on drafts and on some of Gestisoft's exports. Guessing in either case would
    /// produce a field that looks authoritative and is wrong half the time.</para>
    /// </summary>
    private static ActivityType Direction(ParsedMail mail, IReadOnlyCollection<string>? ownAddresses) =>
        ownAddresses is not null
        && mail.From is { Address.Length: > 0 } sender
        && PracticeAddresses.IsOwn(sender.Address, ownAddresses)
            ? ActivityType.OutgoingEmail
            : ActivityType.IncomingEmail;

    private static string? Excerpt(string body)
    {
        var trimmed = body.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= 600 ? trimmed : trimmed[..600].TrimEnd() + "…";
    }
}
