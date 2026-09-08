using Avocado.Server.Features.Backups.Infrastructure;

namespace Avocado.Server.Tests;

/// <summary>
/// « Tous les soirs à 22 h » has to mean something precise on a laptop that is closed at half past
/// six and opened again at nine the next morning, which is most laptops.
/// </summary>
public class BackupScheduleTests
{
    private static readonly BackupSchedule AtTen = new(IsEnabled: true, Hour: 22);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 3, day, hour, minute, 0, TimeSpan.FromHours(1));

    [Fact]
    public void IsNotDuringTheWorkingDay()
    {
        // Captured last night. Nothing should touch her disk at eleven in the morning.
        Assert.False(AtTen.IsDue(At(13, 22, 4), At(14, 11)));
    }

    [Fact]
    public void ComesRoundAtTheHourSheChose()
    {
        Assert.False(AtTen.IsDue(At(13, 22, 4), At(14, 21, 59)));
        Assert.True(AtTen.IsDue(At(13, 22, 4), At(14, 22, 0)));
    }

    [Fact]
    public void RunsOnceANightRatherThanEveryThirtySeconds()
    {
        // The pass beats every thirty seconds. Having just captured at 22:00:30, it must say no for
        // the rest of the night, or the practice is read continuously until morning.
        Assert.False(AtTen.IsDue(At(14, 22, 0), At(14, 23, 30)));
        Assert.False(AtTen.IsDue(At(14, 22, 0), At(15, 8)));
    }

    [Fact]
    public void CatchesUpAfterANightTheMachineWasOff()
    {
        // Closed at six on the 13th, opened at nine on the 15th: two windows went by unattended.
        // Waiting for the next one would mean two days with no copy of the documents, quietly.
        Assert.True(AtTen.IsDue(At(13, 18), At(15, 9)));
    }

    [Fact]
    public void RunsTheFirstTimeItIsEverAsked()
    {
        Assert.True(AtTen.IsDue(null, At(14, 11)));
    }

    [Fact]
    public void StaysOutOfTheWayWhenSheTurnsItOff()
    {
        var off = AtTen with { IsEnabled = false };

        Assert.False(off.IsDue(null, At(14, 23)));
    }

    [Fact]
    public void SurvivesAnHourThatCannotExist()
    {
        // A settings row is text, and text can say 99. Clamping keeps the backups running; refusing
        // would stop them, which is the one outcome worth avoiding.
        Assert.Equal(23, new BackupSchedule(true, 99).EffectiveHour);
        Assert.Equal(0, new BackupSchedule(true, -3).EffectiveHour);
    }

    [Fact]
    public void TreatsMidnightAsAnHourLikeAnyOther()
    {
        var atMidnight = new BackupSchedule(IsEnabled: true, Hour: 0);

        Assert.False(atMidnight.IsDue(At(14, 0, 30), At(14, 23)));
        Assert.True(atMidnight.IsDue(At(14, 0, 30), At(15, 0, 1)));
    }
}
