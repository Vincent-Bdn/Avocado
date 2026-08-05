using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Matters.Infrastructure;

internal sealed class MatterConfiguration : IEntityTypeConfiguration<Matter>
{
    public void Configure(EntityTypeBuilder<Matter> builder)
    {
        builder.ToTable("matters");
        builder.HasKey(matter => matter.Id);

        builder.Property(matter => matter.Reference).HasMaxLength(64).IsRequired();
        builder.Property(matter => matter.Name).HasMaxLength(300).IsRequired();
        builder.Property(matter => matter.CourtCaseNumber).HasMaxLength(64);

        builder.Ignore(matter => matter.IsOpen);

        builder.HasIndex(matter => matter.Reference).IsUnique();
        builder.HasIndex(matter => matter.CourtCaseNumber);

        // The secondary panel and every default view filter on "en cours".
        builder.HasIndex(matter => matter.ClosedOn);
    }
}
