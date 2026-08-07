using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Billings.Infrastructure;

internal sealed class BillingCostConfiguration : IEntityTypeConfiguration<BillingCost>
{
    public void Configure(EntityTypeBuilder<BillingCost> builder)
    {
        builder.ToTable("billing_costs");
        builder.HasKey(cost => cost.Id);

        builder.Property(cost => cost.Kind).HasMaxLength(100);
        builder.Property(cost => cost.Label).HasMaxLength(400).IsRequired();
        builder.Property(cost => cost.ExternalReference).HasMaxLength(100);

        builder.HasOne(cost => cost.Matter)
            .WithMany()
            .HasForeignKey(cost => cost.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting the confrère's fiche must not delete what she paid him. The amount is accounting;
        // the link to a tiers is a convenience.
        builder.HasOne(cost => cost.Contact)
            .WithMany()
            .HasForeignKey(cost => cost.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        // Deleting a facture releases its costs back to the dossier rather than destroying them:
        // an invoice cancelled is a rétrocession still owed.
        builder.HasOne(cost => cost.Invoice)
            .WithMany()
            .HasForeignKey(cost => cost.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(cost => new { cost.MatterId, cost.Date });
        builder.HasIndex(cost => cost.InvoiceId);
    }
}
