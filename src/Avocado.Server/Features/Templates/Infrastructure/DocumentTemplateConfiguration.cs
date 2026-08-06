using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Templates.Infrastructure;

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates");
        builder.HasKey(template => template.Id);

        builder.Property(template => template.Name).HasMaxLength(200).IsRequired();
        builder.Property(template => template.Kind).HasMaxLength(100);
        builder.Property(template => template.FileName).HasMaxLength(400).IsRequired();
        builder.Property(template => template.BlobSha256).HasMaxLength(64).IsRequired();

        builder.HasIndex(template => template.Kind);
    }
}
