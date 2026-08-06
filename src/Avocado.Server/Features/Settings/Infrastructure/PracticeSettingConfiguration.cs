using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Settings.Infrastructure;

internal sealed class PracticeSettingConfiguration : IEntityTypeConfiguration<PracticeSetting>
{
    public void Configure(EntityTypeBuilder<PracticeSetting> builder)
    {
        builder.ToTable("practice_settings");
        builder.HasKey(setting => setting.Key);

        builder.Property(setting => setting.Key).HasMaxLength(120);
        builder.Property(setting => setting.Value).HasMaxLength(2000).IsRequired();
    }
}
