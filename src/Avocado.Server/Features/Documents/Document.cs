using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Documents;

/// <summary>
/// Any file attached to a matter. The bytes live in the encrypted blob store; this row holds only the
/// reference and the metadata.
/// <para>
/// A document <em>becomes</em> a <b>pièce</b> when it is given a number and a libellé. That is a 1:1
/// relationship, hence two nullable columns rather than a separate table. In French procedure pièces
/// are the evidence communicated to the other side, numbered and cited in conclusions (« la pièce
/// n° 7 »); conclusions and correspondence with one's own client are never pièces.
/// </para>
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    /// <summary>The journal entry that brought the file in, when there was one.</summary>
    public Guid? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Hex SHA-256 of the plaintext — the <c>BlobReference</c> the vault stores under.</summary>
    public string BlobSha256 { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? MimeType { get; set; }

    /// <summary>The date on the document itself, which is rarely the date it was filed.</summary>
    public DateOnly? DocumentDate { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when this document is promoted to a numbered exhibit. Unique within the matter.</summary>
    public int? ExhibitNumber { get; set; }

    /// <summary>
    /// The description written for the judge — « Contrat de travail de M. Dupont du 12 mars 2019 »,
    /// never the file name.
    /// </summary>
    public string? ExhibitLabel { get; set; }

    public bool IsExhibit => ExhibitNumber is not null;
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.BlobSha256).HasMaxLength(64).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(400).IsRequired();
        builder.Property(d => d.MimeType).HasMaxLength(160);
        builder.Property(d => d.ExhibitLabel).HasMaxLength(500);

        builder.Ignore(d => d.IsExhibit);

        builder.HasOne(d => d.Matter)
            .WithMany()
            .HasForeignKey(d => d.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a journal entry must not take its attachments with it.
        builder.HasOne(d => d.Activity)
            .WithMany()
            .HasForeignKey(d => d.ActivityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.MatterId);
        builder.HasIndex(d => d.BlobSha256);

        // Two pièces n° 7 in one dossier would make every citation in the conclusions ambiguous.
        builder.HasIndex(d => new { d.MatterId, d.ExhibitNumber })
            .IsUnique()
            .HasFilter("exhibit_number IS NOT NULL");
    }
}
