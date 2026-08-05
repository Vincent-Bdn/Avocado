using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Matters.Infrastructure;

internal sealed class MatterPartyConfiguration : IEntityTypeConfiguration<MatterParty>
{
    public void Configure(EntityTypeBuilder<MatterParty> builder)
    {
        builder.ToTable("matter_parties");
        builder.HasKey(party => party.Id);

        builder.Property(party => party.Role).HasMaxLength(200);

        builder.HasOne(party => party.Matter)
            .WithMany(matter => matter.Parties)
            .HasForeignKey(party => party.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a contact who is still party to a matter would leave the matter unattributable.
        builder.HasOne(party => party.Contact)
            .WithMany()
            .HasForeignKey(party => party.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(party => new { party.MatterId, party.ContactId }).IsUnique();
        builder.HasIndex(party => party.ContactId);
    }
}
