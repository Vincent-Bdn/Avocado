using Avocado.Server.Features.Contacts.Enums;

namespace Avocado.Server.Features.Contacts.Endpoints.Dtos;

/// <summary>What a list row needs. <see cref="Type"/> drives the avatar shape — round for a personne
/// physique, rounded square for a personne morale.</summary>
public sealed record ContactSummary(
    Guid Id,
    ContactType Type,
    string DisplayName,
    string? Email,
    string? Phone)
{
    public static ContactSummary From(Contact contact) =>
        new(contact.Id, contact.Type, contact.DisplayName, contact.Email, contact.Phone);
}
