using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Contacts.Infrastructure;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(contact => contact.Id);

        // Stored as text: adding a case later must not renumber the existing ones.
        builder.Property(contact => contact.Type).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(contact => contact.Civility).HasMaxLength(32);
        builder.Property(contact => contact.LastName).HasMaxLength(200);
        builder.Property(contact => contact.FirstName).HasMaxLength(200);
        builder.Property(contact => contact.LegalName).HasMaxLength(300);
        builder.Property(contact => contact.Siren).HasMaxLength(14);
        builder.Property(contact => contact.LegalForm).HasMaxLength(100);
        builder.Property(contact => contact.Email).HasMaxLength(320);
        builder.Property(contact => contact.Phone).HasMaxLength(40);

        builder.Ignore(contact => contact.DisplayName);

        builder.Property(contact => contact.AttachedAs).HasMaxLength(200);

        // Named explicitly, or EF invents a second shadow foreign key alongside AttachedToContactId.
        // Deleting the organisation detaches its people rather than deleting them: a gérant outlives
        // the société, and losing their telephone number with it would be a real loss.
        builder.HasOne(contact => contact.AttachedTo)
            .WithMany()
            .HasForeignKey(contact => contact.AttachedToContactId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(contact => contact.LastName);
        builder.HasIndex(contact => contact.LegalName);
        builder.HasIndex(contact => contact.Siren);
    }
}
