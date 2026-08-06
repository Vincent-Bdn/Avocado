using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Billings.Infrastructure;

internal sealed class BillingInvoiceConfiguration : IEntityTypeConfiguration<BillingInvoice>
{
    public void Configure(EntityTypeBuilder<BillingInvoice> builder)
    {
        builder.ToTable("invoices");
        builder.Ignore(invoice => invoice.VarianceCents);
        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.ExternalReference).HasMaxLength(120);

        builder.HasOne(invoice => invoice.Matter)
            .WithMany()
            .HasForeignKey(invoice => invoice.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(invoice => new { invoice.MatterId, invoice.Date });
        builder.HasIndex(invoice => invoice.IsPaid);
    }
}
