using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.TimeEntries.Infrastructure;

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("time_entries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Task).HasMaxLength(400).IsRequired();

        builder.HasOne(entry => entry.Matter)
            .WithMany()
            .HasForeignKey(entry => entry.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a journal entry must not silently delete the billable time attached to it.
        builder.HasOne(entry => entry.Activity)
            .WithMany()
            .HasForeignKey(entry => entry.ActivityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(entry => entry.User)
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deleting a facture releases its hours back to « à facturer » rather than deleting them:
        // an invoice cancelled is work still done.
        builder.HasOne(entry => entry.Invoice)
            .WithMany()
            .HasForeignKey(entry => entry.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(entry => entry.InvoiceId);
        builder.HasIndex(entry => new { entry.MatterId, entry.Date });
        builder.HasIndex(entry => entry.Date);
    }
}
