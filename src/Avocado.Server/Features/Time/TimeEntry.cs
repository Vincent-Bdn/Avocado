using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Time;

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
    /// Logging « appel client, 20 min » should create the activity and the time entry in one
    /// keystroke. In Gestisoft those are two separate screens, which is precisely why solo lawyers
    /// under-record their billable time — this link is the highest-value thing in the model.
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

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("time_entries");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Task).HasMaxLength(400).IsRequired();

        builder.HasOne(t => t.Matter)
            .WithMany()
            .HasForeignKey(t => t.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a journal entry must not silently delete the billable time attached to it.
        builder.HasOne(t => t.Activity)
            .WithMany()
            .HasForeignKey(t => t.ActivityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.MatterId, t.Date });
        builder.HasIndex(t => t.Date);
    }
}
