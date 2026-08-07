using Avocado.Server.Features.Deadlines.Enums;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Deadlines;

/// <summary>
/// An échéance. Date and time are separate because a délai has no time of day while an audience is at
/// 9 h, storing a midnight placeholder would make « aujourd'hui · 17:00 » impossible to render
/// honestly, and the urgency tiers depend on telling those apart.
/// </summary>
public class Deadline
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly? Time { get; set; }

    public DeadlineType Type { get; set; }

    public string Label { get; set; } = string.Empty;

    public int RemindDaysBefore { get; set; }

    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
