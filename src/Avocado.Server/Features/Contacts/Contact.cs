using Avocado.Server.Features.Contacts.Enums;

namespace Avocado.Server.Features.Contacts;

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

    /// <summary>
    /// « Personnes rattachées »: the gérant of a société, the DAF, the spouse in an indivision. A
    /// self-reference rather than a join table, because a person belongs to one organisation at a
    /// time in this context and the alternative buys nothing.
    /// </summary>
    public Guid? AttachedToContactId { get; set; }
    public Contact? AttachedTo { get; set; }

    /// <summary>« Gérant et associé majoritaire », « DAF ». Free text, like every other role.</summary>
    public string? AttachedAs { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayName => Type == ContactType.Organisation
        ? LegalName ?? string.Empty
        : string.Join(' ', new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
