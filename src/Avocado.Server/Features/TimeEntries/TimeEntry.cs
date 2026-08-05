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

    /// <summary>Minutes, not a <c>TimeSpan</c>: SQLite has no duration type worth trusting.</summary>
    public int DurationMinutes { get; set; }

    public string Task { get; set; } = string.Empty;

    public bool IsBillable { get; set; } = true;

    /// <summary>
    /// « Je ne facture que la moitié ». Null falls back to the matter's rate — safely, because that
    /// rate was itself frozen at creation and cannot drift.
    /// </summary>
    public long? HourlyRateCentsOverride { get; set; }

    /// <summary>
    /// The journal entry this time was spent on.
    /// <para>
    /// Logging « appel client, 20 min » creates the activity and the time entry in one keystroke —
    /// the composer's ochre duration chip writes this link. In Gestisoft those are two separate
    /// screens, which is precisely why solo lawyers under-record their billable time.
    /// </para>
    /// </summary>
    public Guid? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long AmountCents(long matterHourlyRateCents) =>
        IsBillable
            ? (HourlyRateCentsOverride ?? matterHourlyRateCents) * DurationMinutes / 60
            : 0;
}
