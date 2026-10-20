using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.TimeEntries.Endpoints;

/// <param name="Minutes">Aligned with <see cref="TimeGrid.Days"/>, so a cell is a lookup by index.</param>
/// <param name="BillableMinutes">Of the row's total. A row entirely non facturable says so.</param>
public sealed record TimeGridRow(
    Guid MatterId,
    string Reference,
    string Name,
    string? ClientName,
    IReadOnlyList<int> Minutes,
    int TotalMinutes,
    int BillableMinutes);

/// <param name="Days">Every day of the window, including the empty ones.</param>
/// <param name="DayMinutes">The column totals, aligned with <paramref name="Days"/>.</param>
public sealed record TimeGrid(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<int> DayMinutes,
    IReadOnlyList<TimeGridRow> Rows,
    int TotalMinutes,
    int BillableMinutes);

/// <summary>
/// Ce qui a été saisi comme temps chaque jour, et sur quel dossier.
///
/// <para>Her own sketch, a table with the days across the top and the dossiers down the side:</para>
/// <code>
///            Lundi 01/01   Mardi 02/01   Mercredi 03/01
/// Dossier A       2h                           1h
/// Dossier B                     4h
/// Dossier C       0,5h                         2h
/// </code>
///
/// <para><b>What it is for is the empty cells.</b> Time that is not recorded is never billed, so the
/// question this answers is not « how much have I worked » — the facturation screen says that — but
/// « which afternoon did I forget to write down ». A dossier she worked on all week with a blank
/// Wednesday is the thing she is looking for, which is why the rows keep their days even when the
/// day is empty, and why the grid is days rather than a list of entries.</para>
///
/// <para>Time entries have always been readable one dossier at a time and never across the practice.
/// That is the whole gap: the information existed, in rows that already carry a date and a matter,
/// and no query ever grouped them the other way round.</para>
/// </summary>
public static class GetTimeGrid
{
    /// <summary>Six weeks. Beyond that the columns stop being days anyone can read on a screen.</summary>
    private const int MostDays = 42;

    public static async Task<IResult> HandleAsync(
        DateOnly? from,
        DateOnly? to,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Monday-based, matching French convention, and the same arithmetic the per-dossier totals
        // already use: « cette semaine » must not reset on Sunday.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var start = from ?? monday;
        var finish = to ?? start.AddDays(6);

        if (finish < start)
        {
            return Problem("La fin de la période est avant son début.");
        }

        var days = finish.DayNumber - start.DayNumber + 1;

        if (days > MostDays)
        {
            return Problem($"Choisissez une période plus courte : {MostDays} jours au maximum.");
        }

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.Date >= start && entry.Date <= finish)
            .Select(entry => new
            {
                entry.MatterId,
                entry.Date,
                entry.DurationMinutes,
                entry.IsBillable,
            })
            .ToListAsync(cancellationToken);

        var columns = Enumerable.Range(0, days).Select(start.AddDays).ToList();

        if (entries.Count == 0)
        {
            return Results.Ok(new TimeGrid(
                start, finish, columns, [.. columns.Select(_ => 0)], [], 0, 0));
        }

        var touched = entries.Select(entry => entry.MatterId).Distinct().ToList();

        // Closed dossiers included: time recorded in a week she then closed the dossier is still time
        // she recorded that week, and leaving it out would make the day totals disagree with the
        // entries behind them.
        var named = await database.Matters
            .AsNoTracking()
            .Where(matter => touched.Contains(matter.Id))
            .Select(matter => new
            {
                matter.Id,
                matter.Reference,
                matter.Name,
                ClientName = ClientNameOf(matter),
            })
            .ToListAsync(cancellationToken);

        var rows = named
            .Select(matter =>
            {
                var mine = entries.Where(entry => entry.MatterId == matter.Id).ToList();

                return new TimeGridRow(
                    matter.Id,
                    matter.Reference,
                    matter.Name,
                    matter.ClientName,
                    [.. columns.Select(day => mine
                        .Where(entry => entry.Date == day)
                        .Sum(entry => entry.DurationMinutes))],
                    mine.Sum(entry => entry.DurationMinutes),
                    mine.Where(entry => entry.IsBillable).Sum(entry => entry.DurationMinutes));
            })
            // Busiest first: with twenty dossiers in a month, the ones she spent the week on are the
            // ones the table is about.
            .OrderByDescending(row => row.TotalMinutes)
            .ThenBy(row => row.Name)
            .ToList();

        return Results.Ok(new TimeGrid(
            start,
            finish,
            columns,
            [.. columns.Select(day => entries.Where(entry => entry.Date == day).Sum(entry => entry.DurationMinutes))],
            rows,
            rows.Sum(row => row.TotalMinutes),
            rows.Sum(row => row.BillableMinutes)));
    }

    /// <summary>One client, deliberately, the first by creation order. As every other list does it.</summary>
    private static string? ClientNameOf(Matter matter) => matter.Parties
        .Where(party => party.IsClient)
        .OrderBy(party => party.Id)
        .Select(party => party.Contact!.Type == ContactType.Organisation
            ? party.Contact!.LegalName
            : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
        .FirstOrDefault();

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Temps passé", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
