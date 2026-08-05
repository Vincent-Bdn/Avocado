using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Deadlines;

/// <summary>The single definition of the four urgency tiers.</summary>
public static class DeadlineUrgencyRule
{
    public const int ThisWeekDays = 7;

    /// <summary>Horizon of the accueil's deadline list.</summary>
    public const int UpcomingDays = 30;

    public static DeadlineUrgency For(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;

        return days switch
        {
            < 0 => DeadlineUrgency.Overdue,
            0 => DeadlineUrgency.Today,
            <= ThisWeekDays => DeadlineUrgency.ThisWeek,
            _ => DeadlineUrgency.Later,
        };
    }
}
