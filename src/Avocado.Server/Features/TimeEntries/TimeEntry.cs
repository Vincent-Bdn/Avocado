using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.TimeEntries;

/// <summary>Temps passé.</summary>
public class TimeEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>
    /// When the work started, where that is known, the entry list renders « 13/03 · 16:42 » and the
    /// chronometer « démarré à 16:24 ». Nullable and separate from <see cref="Date"/>, like
    /// <c>Deadline</c>: a manually recorded « 1 h 30 le 13/03 » genuinely has no time of day, and a
    /// midnight placeholder would be a lie the UI would then have to render.
    /// </summary>
    public TimeOnly? StartedAt { get; set; }

    /// <summary>Minutes, not a <c>TimeSpan</c>: SQLite has no duration type worth trusting.</summary>
    public int DurationMinutes { get; set; }

    public string Task { get; set; } = string.Empty;

    public bool IsBillable { get; set; } = true;

    /// <summary>
    /// « Je ne facture que la moitié ». Null falls back to the matter's rate, safely, because that
    /// rate was itself frozen at creation and cannot drift.
    /// </summary>
    public long? HourlyRateCentsOverride { get; set; }

    /// <summary>
    /// The journal entry this time was spent on.
    /// <para>
    /// Logging « appel client, 20 min » creates the activity and the time entry in one keystroke,
    /// the composer's ochre duration chip writes this link. In Gestisoft those are two separate
    /// screens, which is precisely why solo lawyers under-record their billable time.
    /// </para>
    /// </summary>
    public Guid? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>
    /// Whose time this was. The rate still comes from the matter, not from here: a convention
    /// d'honoraires fixes one rate for the dossier. Recording the person is what would let a
    /// per-person rate be introduced later without rewriting history.
    /// </summary>
    public Guid? UserId { get; set; }
    public Users.User? User { get; set; }

    /// <summary>
    /// The facture this hour was billed on, once it has been.
    /// <para>
    /// This is what makes « reste à facturer » mean something after the second invoice. Lawyers
    /// rarely bill everything at once, so the question is never « what has this dossier earned » but
    /// « what have I earned since the last facture », and answering that by date is a heuristic that
    /// breaks the first time an old entry is corrected. A hard link does not.
    /// </para>
    /// </summary>
    public Guid? InvoiceId { get; set; }
    public Billings.BillingInvoice? Invoice { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long AmountCents(long matterHourlyRateCents) =>
        IsBillable
            ? (HourlyRateCentsOverride ?? matterHourlyRateCents) * DurationMinutes / 60
            : 0;
}
