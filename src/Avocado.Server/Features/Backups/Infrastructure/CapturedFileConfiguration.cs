using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Backups.Infrastructure;

internal sealed class CapturedFileConfiguration : IEntityTypeConfiguration<CapturedFile>
{
    public void Configure(EntityTypeBuilder<CapturedFile> builder)
    {
        builder.ToTable("captured_files");
        builder.HasKey(file => file.Id);

        // Long: Windows allows a 32 767-character path with the long-path flag, and a dossier nested
        // « 2024 / Durand / Procédure / Pièces adverses / … » gets deep faster than it looks.
        builder.Property(file => file.RelativePath).HasMaxLength(1000).IsRequired();
        builder.Property(file => file.BlobSha256).HasMaxLength(64).IsRequired();

        // One row per file per dossier. The capture looks itself up by this on every pass, once per
        // file, so it is the index that decides whether a nightly run over thirteen thousand files
        // takes seconds or minutes.
        builder.HasIndex(file => new { file.MatterId, file.RelativePath }).IsUnique();

        // Deleting a dossier takes its manifest with it. The blobs stay until they are swept, which
        // is deliberate: an accidental deletion is one of the things a sauvegarde exists for.
        builder.HasOne<Matters.Matter>()
            .WithMany()
            .HasForeignKey(file => file.MatterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
