namespace Avocado.Server.Features.Matters.Endpoints.Dtos;

/// <param name="Reference">
/// Optional on create: left empty, the server allocates the next <c>YYYY-NNNN</c>. Supplied, it carries
/// an existing reference over from whatever she used before.
/// </param>
/// <param name="ClientContactId">
/// A dossier is opened for someone. Required, so no matter can exist without an answer to "who do I
/// bill". Further parties are added afterwards.
/// </param>
public sealed record MatterInput(
    string Name,
    Guid ClientContactId,
    string? Reference,
    string? Description,
    DateOnly? OpenedOn,
    long? HourlyRateCents,
    string? CourtCaseNumber,
    string? Classification = null,
    string? Court = null,
    bool IsFavourite = false)
{
    public string? Validate() => this switch
    {
        { Name: var name } when string.IsNullOrWhiteSpace(name) =>
            "Le nom du dossier est obligatoire.",
        { ClientContactId: var client } when client == Guid.Empty =>
            "Un dossier doit avoir un client.",
        { HourlyRateCents: < 0 } =>
            "Le taux horaire ne peut pas être négatif.",
        // The two litigation fields only mean anything on a contentieux, and silently keeping a stale
        // n° RG on a dossier reclassified as conseil is how a header ends up lying.
        { Classification: var kind, CourtCaseNumber: not null } when !IsLitigation(kind) =>
            "Un n° RG ne se saisit que sur un dossier contentieux.",
        { Classification: var kind, Court: not null } when !IsLitigation(kind) =>
            "Une juridiction ne se saisit que sur un dossier contentieux.",
        _ => null,
    };

    /// <summary>
    /// The one word the application interprets. Everything else in <c>Classification</c> is free text
    /// the practice can invent, and a practice that writes « Arbitrage » simply gets no RG field.
    /// </summary>
    public static bool IsLitigation(string? classification) =>
        string.Equals(classification, "Contentieux", StringComparison.OrdinalIgnoreCase);
}

/// <param name="Role">
/// Free text, and often long: « Avocat de la partie adverse au barreau de Villefranche ». The one
/// thing the application interprets is <paramref name="IsClient"/>.
/// </param>
public sealed record MatterPartyInput(Guid ContactId, bool IsClient, string? Role)
{
    public string? Validate() => ContactId == Guid.Empty ? "Choisissez un tiers." : null;
}
