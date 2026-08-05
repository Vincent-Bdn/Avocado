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
    string? CourtCaseNumber)
{
    public string? Validate() => this switch
    {
        { Name: var name } when string.IsNullOrWhiteSpace(name) =>
            "Le nom du dossier est obligatoire.",
        { ClientContactId: var client } when client == Guid.Empty =>
            "Un dossier doit avoir un client.",
        { HourlyRateCents: < 0 } =>
            "Le taux horaire ne peut pas être négatif.",
        _ => null,
    };
}
