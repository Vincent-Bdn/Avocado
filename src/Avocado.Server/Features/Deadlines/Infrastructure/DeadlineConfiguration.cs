using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Deadlines.Infrastructure;

internal sealed class DeadlineConfiguration : IEntityTypeConfiguration<Deadline>
{
    public void Configure(EntityTypeBuilder<Deadline> builder)
    {
        builder.ToTable("deadlines");
        builder.HasKey(deadline => deadline.Id);

        builder.Property(deadline => deadline.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(deadline => deadline.Label).HasMaxLength(300).IsRequired();

        builder.HasOne(deadline => deadline.Matter)
            .WithMany()
            .HasForeignKey(deadline => deadline.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Drives the accueil ("30 prochains jours"), the ICS feed and the rail's urgency dot — all of
        // which scan by date across every matter.
        builder.HasIndex(deadline => new { deadline.IsDone, deadline.Date });
        builder.HasIndex(deadline => deadline.MatterId);
    }
}
