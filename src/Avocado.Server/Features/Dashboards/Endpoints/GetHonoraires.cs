using Avocado.Server.Data;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Avocado.Server.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Dashboards.Endpoints;

/// <param name="Label">What the window should call this period, « septembre 2026 », « exercice 2026 ».</param>
/// <param name="Matters">
/// Which dossiers the period is made of, worst payer first. The answer to « à quels dossiers se
/// rapportent ces 1 200 € ».
/// </param>
public sealed record HonorairesView(
    DateOnly From,
    DateOnly To,
    string Label,
    DashboardHonoraires Honoraires,
    IReadOnlyList<HonoraireMatter> Matters);

/// <summary>
/// La facturation sur une période, et les dossiers derrière.
///
/// <para><b>One endpoint, two uses.</b> It serves the period selector — mois, trimestre, exercice,
/// dates libres — and it serves the drill-down, because clicking septembre on the chart is asking for
/// the period « septembre ». Both give back the same shape, so the card below the chart always foots
/// to what is being looked at, which is what a second endpoint would have let drift.</para>
///
/// <para>The dates are inclusive and default to the accueil's own window, so a call with no
/// parameters answers exactly what the accueil shows.</para>
/// </summary>
public static class GetHonoraires
{
    /// <summary>Beyond this the buckets stop being months anyone reads. Twenty years of a practice.</summary>
    private const int MostMonths = 240;

    public static async Task<IResult> HandleAsync(
        DateOnly? from,
        DateOnly? to,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var start = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var finish = to ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        if (finish < start)
        {
            return Problem("La fin de la période est avant son début.");
        }

        if (Months(start, finish) > MostMonths)
        {
            return Problem("Choisissez une période plus courte : vingt ans au maximum.");
        }

        var honoraires = await HonorairesQuery.ForPeriodAsync(database, start, finish, cancellationToken);
        var matters = await HonorairesDetailQuery.ForPeriodAsync(database, start, finish, cancellationToken);

        return Results.Ok(new HonorairesView(
            start, finish, Label(start, finish, today), honoraires, matters));
    }

    /// <summary>
    /// The periods the window offers, computed here because the exercice's start is a practice
    /// setting and the window has no business knowing where it is stored.
    /// </summary>
    public static async Task<IResult> ChoicesAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var settings = await database.PracticeSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        // Her own exercice, not the calendar year: a practice that opened in September closes its
        // books in August, and the date is a setting for exactly that reason. Exercices.Containing
        // already knows how to find the one a day falls in, 29 February included.
        var exercice = Exercices.Containing(today, Exercices.StartFrom(settings));
        var previous = Exercices.Containing(exercice.From.AddDays(-1), Exercices.StartFrom(settings));

        var quarter = new DateOnly(today.Year, (((today.Month - 1) / 3) * 3) + 1, 1);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);

        return Results.Ok(new[]
        {
            Choice("Ce mois-ci", thisMonth, EndOfMonth(thisMonth)),
            Choice("Mois dernier", thisMonth.AddMonths(-1), EndOfMonth(thisMonth.AddMonths(-1))),
            Choice("Ce trimestre", quarter, EndOfMonth(quarter.AddMonths(2))),
            // Ends today rather than on the exercice's last day: it is in progress, and a figure
            // running to next August would read as a year that has happened.
            Choice("Exercice en cours", exercice.From, today),
            Choice("Exercice précédent", previous.From, previous.To),
            Choice("12 derniers mois", thisMonth.AddMonths(-11), EndOfMonth(thisMonth)),
        });
    }

    private static object Choice(string label, DateOnly from, DateOnly to) =>
        new { label, from, to };

    private static DateOnly EndOfMonth(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    private static int Months(DateOnly from, DateOnly to) =>
        (((to.Year - from.Year) * 12) + to.Month - from.Month) + 1;

    /// <summary>
    /// A name for the period, so the card's heading is a sentence rather than two dates.
    ///
    /// <para>A whole single month is « septembre 2026 », a whole single year « 2026 ». Anything else
    /// is said as the two dates, because inventing a name for « du 1er janvier au 12 mars » would be
    /// inventing a meaning it does not have.</para>
    /// </summary>
    private static string Label(DateOnly from, DateOnly to, DateOnly today)
    {
        var wholeMonth = from.Day == 1
            && from.Year == to.Year
            && from.Month == to.Month
            && to.Day == DateTime.DaysInMonth(to.Year, to.Month);

        if (wholeMonth)
        {
            return $"{MonthNames[from.Month - 1]} {from.Year}";
        }

        if (from is { Day: 1, Month: 1 } && to is { Month: 12, Day: 31 } && from.Year == to.Year)
        {
            return from.Year.ToString();
        }

        // The month in progress is the one case where « au 20 octobre » is noise: it is today.
        if (from.Day == 1 && from.Year == to.Year && from.Month == to.Month && to == today)
        {
            return $"{MonthNames[from.Month - 1]} {from.Year}, en cours";
        }

        return $"du {Spelled(from)} au {Spelled(to)}";
    }

    private static string Spelled(DateOnly day) =>
        $"{(day.Day == 1 ? "1er" : day.Day.ToString())} {MonthNames[day.Month - 1]} {day.Year}";

    /// <summary>
    /// Built from the parts rather than from the culture: the backend runs with
    /// InvariantGlobalization, and month names are content here, not formatting.
    /// </summary>
    private static readonly string[] MonthNames =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Facturation", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
