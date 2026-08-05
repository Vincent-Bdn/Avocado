using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Activities;

/// <summary>
/// Direction is folded in rather than carried as a separate field. It is meaningless for a call or a
/// note, but for letters « envoyé le 12/03 » versus « reçu le 15/03 » starts délais and evidences
/// diligence — so it lives where it actually matters.
/// </summary>
public enum ActivityType
{
    Call,
    IncomingEmail,
    OutgoingEmail,
    IncomingLetter,
    OutgoingLetter,
    Meeting,
    Note,
    Hearing,
    Other,
}

/// <summary>
/// One event in a matter's chronology — « le suivi ». Adding one must be the fastest interaction in
/// the application.
/// </summary>
public class Activity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public ActivityType Type { get; set; }

    /// <summary>Who it was with, when that is known.</summary>
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.Subject).HasMaxLength(300);

        builder.HasOne(a => a.Matter)
            .WithMany()
            .HasForeignKey(a => a.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Contact)
            .WithMany()
            .HasForeignKey(a => a.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        // The journal is always read newest-first for one matter.
        builder.HasIndex(a => new { a.MatterId, a.OccurredAt });
    }
}
