namespace Avocado.Server.Features.TimeEntries.Endpoints.Dtos;

/// <param name="DurationMinutes">
/// Already parsed. The field accepts « 1h30 », « 90 » and « 1,5 »; that is a formatting concern and
/// stays in the UI.
/// </param>
/// <param name="HourlyRateCentsOverride">The `＋ taux dérogatoire` chip. Null uses the matter's rate.</param>
public sealed record TimeEntryInput(
    DateOnly Date,
    TimeOnly? StartedAt,
    string Task,
    int DurationMinutes,
    bool IsBillable = true,
    long? HourlyRateCentsOverride = null)
{
    public string? Validate() => this switch
    {
        { Task: var task } when string.IsNullOrWhiteSpace(task) =>
            "La description de la tâche est obligatoire.",
        { DurationMinutes: <= 0 } =>
            "La durée doit être positive.",
        { DurationMinutes: > 24 * 60 } =>
            "La durée ne peut pas dépasser 24 heures.",
        { HourlyRateCentsOverride: < 0 } =>
            "Un taux dérogatoire ne peut pas être négatif.",
        _ => null,
    };
}
