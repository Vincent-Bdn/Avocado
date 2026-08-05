using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Contacts;

public enum ContactType
{
    /// <summary>Personne physique.</summary>
    Individual,

    /// <summary>Personne morale.</summary>
    Organisation,
}

/// <summary>
/// A tiers: anyone in the address book. The same contact is a client on one matter and the opposing
/// party on another, so roles live on <c>MatterParty</c>, never here.
/// </summary>
public class Contact
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public ContactType Type { get; set; }

    // Personne physique
    public string? Civility { get; set; }
    public string? LastName { get; set; }
    public string? FirstName { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    // Personne morale
    public string? LegalName { get; set; }
    public string? Siren { get; set; }
    public string? LegalForm { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayName => Type == ContactType.Organisation
        ? LegalName ?? string.Empty
        : string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);

        // Stored as text: adding a case later must not renumber the existing ones.
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(c => c.Civility).HasMaxLength(32);
        builder.Property(c => c.LastName).HasMaxLength(200);
        builder.Property(c => c.FirstName).HasMaxLength(200);
        builder.Property(c => c.LegalName).HasMaxLength(300);
        builder.Property(c => c.Siren).HasMaxLength(14);
        builder.Property(c => c.LegalForm).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(320);
        builder.Property(c => c.Phone).HasMaxLength(40);

        builder.Ignore(c => c.DisplayName);

        builder.HasIndex(c => c.LastName);
        builder.HasIndex(c => c.LegalName);
        builder.HasIndex(c => c.Siren);
    }
}
