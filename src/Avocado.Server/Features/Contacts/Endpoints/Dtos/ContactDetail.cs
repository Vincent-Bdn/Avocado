using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Contacts.Enums;

namespace Avocado.Server.Features.Contacts.Endpoints.Dtos;

/// <param name="Role">
/// Free text, per dossier, and often long. The full wording goes in the row's <c>title</c>, shortening
/// it automatically destroys its meaning.
/// </param>
/// <param name="IsClient">
/// The one role the application interprets: only client relations feed billing. The screen groups on
/// exactly this, « Relations client, facturables » against « Autres rôles, non facturables ».
/// </param>
public sealed record ContactRole(
    Guid MatterId,
    string MatterReference,
    string MatterName,
    bool MatterIsOpen,
    bool IsClient,
    string? Role);

/// <summary>« Derniers échanges », tous dossiers confondus.</summary>
public sealed record ContactExchange(
    Guid ActivityId,
    Guid MatterId,
    string MatterReference,
    string MatterName,
    ActivityType Type,
    DateTimeOffset OccurredAt,
    string? Summary);

/// <param name="ClientSince">
/// « client depuis 11/2025 », the opening date of their earliest client matter. Null when they have
/// never been a client, which the screen states plainly rather than rendering an empty group.
/// </param>
/// <param name="Function">« Gérant et associé majoritaire », « DAF ». Free text, like every role.</param>
public sealed record ContactAttachment(
    Guid Id,
    ContactType Type,
    string DisplayName,
    string? Function,
    string? Email,
    string? Phone);

public sealed record ContactDetail(
    Guid Id,
    ContactType Type,
    string DisplayName,
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
    string? Notes,
    int MatterCount,
    int ClientMatterCount,
    DateOnly? ClientSince,
    IReadOnlyList<ContactRole> Roles,
    IReadOnlyList<ContactExchange> RecentExchanges,
    /// <summary>« Personnes rattachées »: the gérant, the DAF, the spouse.</summary>
    IReadOnlyList<ContactAttachment> AttachedPeople,
    /// <summary>« Rattachement »: the organisation this person belongs to, when there is one.</summary>
    ContactAttachment? AttachedTo);
