using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Deadlines;

public enum DeadlineType
{
    /// <summary>Audience.</summary>
    Hearing,

    /// <summary>Délai de procédure.</summary>
    ProceduralDeadline,

    Appointment,
    Other,
}

/// <summary>
/// An échéance. Date and time are separate because a délai has no time of day while an audience is at
/// 9 h — storing a midnight placeholder would make "aujourd'hui · 17:00" impossible to render honestly.
/// </summary>
public class Deadline
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly? Time { get; set; }

    public DeadlineType Type { get; set; }

    public string Label { get; set; } = string.Empty;

    public int RemindDaysBefore { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class DeadlineConfiguration : IEntityTypeConfiguration<Deadline>
{
    public void Configure(EntityTypeBuilder<Deadline> builder)
    {
        builder.ToTable("deadlines");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(d => d.Label).HasMaxLength(300).IsRequired();

        builder.HasOne(d => d.Matter)
            .WithMany()
            .HasForeignKey(d => d.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Drives the accueil ("30 prochains jours") and the ICS feed, both of which scan by date
        // across every matter.
        builder.HasIndex(d => new { d.IsDone, d.Date });
        builder.HasIndex(d => d.MatterId);
    }
}
