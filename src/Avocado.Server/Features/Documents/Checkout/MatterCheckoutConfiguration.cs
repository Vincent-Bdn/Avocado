using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Documents.Checkout;

internal sealed class MatterCheckoutConfiguration : IEntityTypeConfiguration<MatterCheckout>
{
    public void Configure(EntityTypeBuilder<MatterCheckout> builder)
    {
        builder.ToTable("matter_checkouts");
        builder.HasKey(checkout => checkout.Id);

        builder.Property(checkout => checkout.FolderPath).HasMaxLength(500).IsRequired();

        // No cap: one entry per document, and a dossier can hold hundreds.
        builder.Property(checkout => checkout.Manifest).IsRequired();

        // One folder per dossier. Opening one twice would hand out two copies of the same documents
        // and make "which one is the truth" a question nobody can answer.
        builder.HasIndex(checkout => checkout.MatterId).IsUnique();
    }
}
