using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Deadlines.Endpoints.Dtos;

public sealed record DeadlineItem(
    Guid Id,
    DateOnly Date,
    TimeOnly? Time,
    DeadlineType Type,
    string Label,
    int RemindDaysBefore,
    bool IsDone,
    DeadlineUrgency Urgency);

/// <param name="RemindDaysBefore">Zero means no reminder, which is the honest default for a délai
/// she already has in her head.</param>
public sealed record DeadlineInput(
    DateOnly Date,
    TimeOnly? Time,
    DeadlineType Type,
    string Label,
    int RemindDaysBefore = 0,
    bool IsDone = false)
{
    public string? Validate() => this switch
    {
        { Label: var label } when string.IsNullOrWhiteSpace(label) =>
            "L'intitulé de l'échéance est obligatoire.",
        { RemindDaysBefore: < 0 } =>
            "Le rappel ne peut pas être négatif.",
        _ => null,
    };
}
