using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Billings.Infrastructure;

internal sealed class BillingLedgerEntryConfiguration : IEntityTypeConfiguration<BillingLedgerEntry>
{
    public void Configure(EntityTypeBuilder<BillingLedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Label).HasMaxLength(300).IsRequired();

        builder.HasOne(entry => entry.Matter)
            .WithMany()
            .HasForeignKey(entry => entry.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(entry => new { entry.MatterId, entry.Date });
    }
}
