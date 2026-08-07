using Avocado.Server.Features.Activities.Enums;

namespace Avocado.Server.Features.Activities.Endpoints.Dtos;

/// <param name="OccurredAt">
/// Editable and pre-filled with now by the composer: the 11:00 call is usually logged at 17:00.
/// </param>
/// <param name="DurationMinutes">
/// The composer's ochre `＋ temps passé` chip. Supplied, it creates the journal entry and its time
/// entry in one call, logging a call and its billable time in one keystroke is the highest-value
/// interaction in the product, and splitting it across two requests would let one half fail alone.
/// </param>
/// <param name="TrackingNumber">Numéro de suivi. Only meaningful on the two letter types.</param>
public sealed record ActivityInput(
    ActivityType Type,
    DateTimeOffset? OccurredAt,
    Guid? ContactId,
    string? Subject,
    string? Body,
    string? TrackingNumber,
    int? DurationMinutes,
    bool DurationIsBillable = true)
{
    public string? Validate() => this switch
    {
        { Subject: var subject, Body: var body }
            when string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(body) =>
            "Une entrée de journal doit avoir un objet ou un contenu.",
        { DurationMinutes: <= 0 } =>
            "La durée doit être positive.",
        { DurationMinutes: > 24 * 60 } =>
            "La durée ne peut pas dépasser 24 heures.",
        _ => null,
    };
}
