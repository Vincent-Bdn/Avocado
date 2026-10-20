using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Avocado.Server.Features.TimeEntries.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Avocado.Server.Tests.TimeEntries;

/// <summary>
/// Le temps saisi chaque jour, et sur quel dossier.
///
/// <para>What it is for is the empty cells: time that is not recorded is never billed, so the
/// question is « quel après-midi ai-je oublié de noter » rather than « combien ai-je travaillé ».
/// A row that drops its empty days answers a different question, which is why the grid keeps every
/// column of the window whether or not anything landed in it.</para>
/// </summary>
public sealed class TimeGridTests : IDisposable
{
    private readonly TestVault _vault = new();

    /// <summary>Monday 5 January 2026 through Friday the 9th, which is the week the sketch uses.</summary>
    private static readonly DateOnly Monday = new(2026, 1, 5);

    private sealed class FrozenClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private Matter Dossier(string name) => _vault.Save(new Matter
    {
        Reference = $"2026-{name.GetHashCode(StringComparison.Ordinal) & 0xFFF:0000}",
        Name = name,
        OpenedOn = new DateOnly(2026, 1, 2),
        HourlyRateCents = 20_000,
    });

    private void Heures(Matter matter, DateOnly day, int minutes, bool billable = true) =>
        _vault.Save(new TimeEntry
        {
            MatterId = matter.Id,
            Date = day,
            DurationMinutes = minutes,
            Task = "Travail",
            IsBillable = billable,
        });

    private async Task<TimeGrid> Grid(DateOnly? from = null, DateOnly? to = null, DateOnly? todayIs = null)
    {
        var result = await GetTimeGrid.HandleAsync(
            from, to, _vault.Database, new FrozenClock(todayIs ?? Monday), default);

        return Assert.IsType<TimeGrid>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    /// <summary>Her own table, cell for cell.</summary>
    [Fact]
    public async Task LaysOutTheWeekAsSheDrewIt()
    {
        var a = Dossier("Dossier A");
        var b = Dossier("Dossier B");
        var c = Dossier("Dossier C");

        Heures(a, Monday, 120);
        Heures(a, Monday.AddDays(2), 60);
        Heures(b, Monday.AddDays(1), 240);
        Heures(c, Monday, 30);
        Heures(c, Monday.AddDays(2), 120);
        Heures(c, Monday.AddDays(3), 60);
        Heures(c, Monday.AddDays(4), 30);

        var grid = await Grid(Monday, Monday.AddDays(4));

        Assert.Equal(5, grid.Days.Count);
        Assert.Equal(Monday, grid.Days[0]);
        Assert.Equal(new DateOnly(2026, 1, 9), grid.Days[4]);

        // Busiest first: the dossiers she spent the week on are what the table is about. B and C both
        // come to four hours, and a tie goes alphabetical rather than to whatever order the rows came
        // back in — otherwise the table reshuffles itself between two readings of the same week.
        Assert.Equal(["Dossier B", "Dossier C", "Dossier A"], grid.Rows.Select(row => row.Name));

        var rowC = grid.Rows[1];
        Assert.Equal([30, 0, 120, 60, 30], rowC.Minutes);
        Assert.Equal(240, rowC.TotalMinutes);

        // The column totals, and the grand total, both read off the same rows.
        Assert.Equal([150, 240, 180, 60, 30], grid.DayMinutes);
        Assert.Equal(660, grid.TotalMinutes);
        Assert.Equal(grid.Rows.Sum(row => row.TotalMinutes), grid.TotalMinutes);
    }

    /// <summary>
    /// The blank Wednesday on a dossier she worked all week is the thing she is looking for. A row
    /// that dropped its empty days would answer a different question.
    /// </summary>
    [Fact]
    public async Task KeepsTheEmptyDays()
    {
        var matter = Dossier("Dossier A");
        Heures(matter, Monday, 60);
        Heures(matter, Monday.AddDays(4), 60);

        var grid = await Grid(Monday, Monday.AddDays(4));

        Assert.Equal([60, 0, 0, 0, 60], Assert.Single(grid.Rows).Minutes);
    }

    [Fact]
    public async Task DefaultsToThisWeekStartingOnMonday()
    {
        var matter = Dossier("Dossier A");

        // Recorded on the Thursday; asked on the Thursday, with no dates.
        Heures(matter, Monday.AddDays(3), 90);

        var grid = await Grid(todayIs: Monday.AddDays(3));

        Assert.Equal(Monday, grid.From);
        Assert.Equal(Monday.AddDays(6), grid.To);
        Assert.Equal(7, grid.Days.Count);
        Assert.Equal(90, grid.TotalMinutes);
    }

    /// <summary>A Sunday is still in the week that began on the Monday before it.</summary>
    [Fact]
    public async Task DoesNotResetTheWeekOnSunday()
    {
        var grid = await Grid(todayIs: Monday.AddDays(6));

        Assert.Equal(Monday, grid.From);
    }

    [Fact]
    public async Task SeparatesBillableFromTheRest()
    {
        var matter = Dossier("Dossier A");
        Heures(matter, Monday, 60);
        Heures(matter, Monday, 30, billable: false);

        var grid = await Grid(Monday, Monday);

        // The cell is the time she spent; the row says how much of it can be billed.
        Assert.Equal([90], Assert.Single(grid.Rows).Minutes);
        Assert.Equal(90, grid.Rows[0].TotalMinutes);
        Assert.Equal(60, grid.Rows[0].BillableMinutes);
    }

    [Fact]
    public async Task AnEmptyWeekIsStillAWeekOfColumns()
    {
        var grid = await Grid(Monday, Monday.AddDays(4));

        Assert.Equal(5, grid.Days.Count);
        Assert.Equal([0, 0, 0, 0, 0], grid.DayMinutes);
        Assert.Empty(grid.Rows);
    }

    [Fact]
    public async Task RefusesAWindowNobodyCouldRead()
    {
        var result = await GetTimeGrid.HandleAsync(
            Monday, Monday.AddDays(120), _vault.Database, new FrozenClock(Monday), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task RefusesAnInvertedWindow()
    {
        var result = await GetTimeGrid.HandleAsync(
            Monday, Monday.AddDays(-1), _vault.Database, new FrozenClock(Monday), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    public void Dispose() => _vault.Dispose();
}
