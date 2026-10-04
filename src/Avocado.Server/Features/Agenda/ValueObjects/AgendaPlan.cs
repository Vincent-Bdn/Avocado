namespace Avocado.Server.Features.Agenda.ValueObjects;

public enum AgendaActionKind
{
    /// <summary>Never sent. Insert it and record the id Google gives back.</summary>
    Insert,

    /// <summary>Sent, and something about it has changed since.</summary>
    Update,

    /// <summary>Sent, and the échéance it stood for is gone from Avocado.</summary>
    Delete,
}

/// <param name="Entry">Null on a delete: the échéance no longer exists to describe.</param>
/// <param name="EventId">Null on an insert: Google has not assigned one yet.</param>
public readonly record struct AgendaAction(
    AgendaActionKind Kind,
    Guid DeadlineId,
    AgendaEntry? Entry,
    string? EventId);

/// <summary>
/// What has to be sent to Google, from what Avocado holds and what it believes is already there.
///
/// <para>Pure, and deliberately so. Everything that decides whether a lawyer's calendar is right
/// happens here, and it is reachable without a Google account, a network, or a token: the part that
/// talks to Google only carries out this list.</para>
///
/// <para><b>Unchanged échéances are not resent.</b> A pass runs every few minutes and a practice has
/// a few hundred échéances; rewriting all of them each time would be rate-limited by Google within
/// the hour, and would also mean her agenda showed a « modifié il y a 2 minutes » on everything for
/// ever.</para>
/// </summary>
public static class AgendaPlan
{
    /// <param name="wanted">Échéances that should be in the calendar: those of dossiers en cours.</param>
    /// <param name="pushed">What Avocado believes is already there.</param>
    /// <param name="live">
    /// Every échéance Avocado still holds, closed dossiers included.
    ///
    /// <para>What separates « she deleted it » from « the dossier was closed ». Closing hides the
    /// échéances in Avocado; deleting the events would erase last year's audiences from a record of
    /// where she actually was. So a closed dossier stops being updated and keeps what it has, and
    /// only a row that has genuinely gone is removed.</para>
    /// </param>
    public static IReadOnlyList<AgendaAction> For(
        IReadOnlyCollection<AgendaEntry> wanted,
        IReadOnlyCollection<AgendaEvent> pushed,
        IReadOnlySet<Guid> live)
    {
        var byDeadline = pushed.ToDictionary(evt => evt.DeadlineId);
        var actions = new List<AgendaAction>();

        foreach (var entry in wanted)
        {
            if (!byDeadline.TryGetValue(entry.DeadlineId, out var existing))
            {
                actions.Add(new AgendaAction(AgendaActionKind.Insert, entry.DeadlineId, entry, null));
                continue;
            }

            if (existing.Fingerprint != entry.Fingerprint)
            {
                actions.Add(new AgendaAction(
                    AgendaActionKind.Update, entry.DeadlineId, entry, existing.GoogleEventId));
            }
        }

        // An échéance deleted in the dossier leaves her agenda too: leaving it there would put her
        // in a tribunal on a date nobody is expecting her.
        actions.AddRange(pushed
            .Where(evt => !live.Contains(evt.DeadlineId))
            .Select(evt => new AgendaAction(
                AgendaActionKind.Delete, evt.DeadlineId, null, evt.GoogleEventId)));

        return actions;
    }
}
