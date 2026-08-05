using Avocado.Server.Features.Contacts.Enums;

namespace Avocado.Server.Features.Contacts.Endpoints.Dtos;

/// <summary>Shared by create and update, so the two can never drift apart.</summary>
public sealed record ContactInput(
    ContactType Type,
    string? Civility,
    string? LastName,
    string? FirstName,
    DateOnly? DateOfBirth,
    string? LegalName,
    string? Siren,
    string? LegalForm,
    string? Email,
    string? Phone,
    string? Address,
    string? Notes)
{
    /// <summary>Returns the French message to show, or null when the input is acceptable.</summary>
    public string? Validate() => Type switch
    {
        ContactType.Individual when string.IsNullOrWhiteSpace(LastName) =>
            "Le nom est obligatoire pour une personne physique.",
        ContactType.Organisation when string.IsNullOrWhiteSpace(LegalName) =>
            "La raison sociale est obligatoire pour une personne morale.",
        // Nine digits. The annuaire returns it grouped ("552 100 554"), so normalise before counting.
        ContactType.Organisation when Siren is not null && Digits(Siren).Length is not (0 or 9) =>
            "Un SIREN comporte 9 chiffres.",
        _ => null,
    };

    public void ApplyTo(Contact contact)
    {
        contact.Type = Type;
        contact.Civility = Civility;
        contact.LastName = LastName;
        contact.FirstName = FirstName;
        contact.DateOfBirth = DateOfBirth;
        contact.LegalName = LegalName;
        contact.Siren = Siren;
        contact.LegalForm = LegalForm;
        contact.Email = Email;
        contact.Phone = Phone;
        contact.Address = Address;
        contact.Notes = Notes;
    }

    private static string Digits(string value) => new([.. value.Where(char.IsDigit)]);
}
