using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Activities.Infrastructure;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");
        builder.HasKey(activity => activity.Id);

        builder.Property(activity => activity.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(activity => activity.Subject).HasMaxLength(300);
        builder.Property(activity => activity.TrackingNumber).HasMaxLength(64);

        builder.HasOne(activity => activity.Matter)
            .WithMany()
            .HasForeignKey(activity => activity.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a contact must not erase the history of dealing with them.
        builder.HasOne(activity => activity.Contact)
            .WithMany()
            .HasForeignKey(activity => activity.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deactivating a user must not erase what they recorded.
        builder.HasOne(activity => activity.User)
            .WithMany()
            .HasForeignKey(activity => activity.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // The journal is always read newest-first for one matter.
        builder.HasIndex(activity => new { activity.MatterId, activity.OccurredAt });
    }
}
