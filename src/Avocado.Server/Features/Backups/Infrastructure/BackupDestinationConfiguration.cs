using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Backups.Infrastructure;

internal sealed class BackupDestinationConfiguration : IEntityTypeConfiguration<BackupDestination>
{
    public void Configure(EntityTypeBuilder<BackupDestination> builder)
    {
        builder.ToTable("backup_destinations");
        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Kind).HasMaxLength(40).IsRequired();
        builder.Property(destination => destination.Label).HasMaxLength(120).IsRequired();
        builder.Property(destination => destination.Path).HasMaxLength(500);
        builder.Property(destination => destination.RemoteFolderId).HasMaxLength(200);
        builder.Property(destination => destination.LastError).HasMaxLength(1000);

        // No length cap: a refresh token has no contractual maximum, and truncating one silently
        // produces a destination that authenticates until the day it does not.
        builder.Property(destination => destination.Secret);

        builder.HasIndex(destination => destination.IsEnabled);
    }
}
