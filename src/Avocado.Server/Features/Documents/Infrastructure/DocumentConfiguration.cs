using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Documents.Infrastructure;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(document => document.Id);

        builder.Property(document => document.BlobSha256).HasMaxLength(64).IsRequired();
        builder.Property(document => document.FileName).HasMaxLength(400).IsRequired();
        builder.Property(document => document.MimeType).HasMaxLength(160);
        builder.Property(document => document.ExhibitLabel).HasMaxLength(500);

        builder.Ignore(document => document.IsExhibit);

        builder.HasOne(document => document.Matter)
            .WithMany()
            .HasForeignKey(document => document.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a journal entry must not take its attachments with it.
        builder.HasOne(document => document.Activity)
            .WithMany()
            .HasForeignKey(document => document.ActivityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(document => document.MatterId);
        builder.HasIndex(document => document.BlobSha256);

        // Two pièces n° 7 in one dossier would make every citation in the conclusions ambiguous.
        builder.HasIndex(document => new { document.MatterId, document.ExhibitNumber })
            .IsUnique()
            .HasFilter("exhibit_number IS NOT NULL");
    }
}
