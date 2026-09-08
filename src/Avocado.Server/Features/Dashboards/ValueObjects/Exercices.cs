using Avocado.Server.Features.Settings;

namespace Avocado.Server.Features.Dashboards.ValueObjects;

/// <param name="Label">« 2026 - 2027 », or « 2026 » when the exercice is the calendar year.</param>
/// <param name="IsCurrent">The one running now. Its figure is a partial year and says so.</param>
public sealed record Exercice(string Label, DateOnly From, DateOnly To, long InvoicedCents, bool IsCurrent);

/// <summary>
/// Le chiffre d'affaires, par exercice.
///
/// <para>She asked for « le montant total facturé HT depuis le 1er septembre puis pour chaque année
/// complète ». That is an exercice comptable, and the first of September is hers: a practice that
/// opened in September closes its books in August. So the date is a setting rather than a constant,
/// because the next lawyer to install this opened in January, and « a new release for a label » is
/// exactly what a hard-coded date costs.</para>
///
/// <para>HT throughout, from the facture's own <c>AmountExclVatCents</c>. Avocado issues no invoice
/// and computes no TVA, so HT is the only figure it can state honestly, and it is the one an
/// accountant asks for anyway.</para>
/// </summary>
public static class Exercices
{
    /// <summary>« 09-01 », her first of September. Month and day, no year.</summary>
    public static (int Month, int Day) StartFrom(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(PracticeSettingKeys.ExerciceStart, out var written)
            && written.Split('-') is [var month, var day]
            && int.TryParse(month, out var parsedMonth)
            && int.TryParse(day, out var parsedDay)
            && parsedMonth is >= 1 and <= 12
            && parsedDay is >= 1 and <= 28)
        {
            return (parsedMonth, parsedDay);
        }

        return (1, 1);
    }

    /// <summary>
    /// The exercice containing <paramref name="date"/>, as a half-open range on its last day.
    ///
    /// <para>Capped at the 28th when it is read, so 29 February can never be an exercice's first day
    /// and three years in four silently shift it.</para>
    /// </summary>
    public static (DateOnly From, DateOnly To) Containing(DateOnly date, (int Month, int Day) start)
    {
        var candidate = new DateOnly(date.Year, start.Month, start.Day);
        var from = date >= candidate ? candidate : candidate.AddYears(-1);

        return (from, from.AddYears(1).AddDays(-1));
    }

    /// <summary>
    /// « 2026 » for a calendar exercice, « 2026 - 2027 » for one that straddles.
    ///
    /// <para>Not a formatted date range: what she wants on screen is the name accountants use for the
    /// year, and « 01/09/2026 au 31/08/2027 » is four times the width to say the same thing.</para>
    /// </summary>
    public static string Label(DateOnly from, DateOnly to) =>
        from.Year == to.Year ? from.Year.ToString() : $"{from.Year} - {to.Year}";

    /// <summary>
    /// Every exercice from the first facture to today, newest first, including empty ones in between.
    ///
    /// <para>A year with nothing in it is a real answer and gets a row: a gap in the list would read
    /// as a missing year rather than as a quiet one.</para>
    /// </summary>
    public static IReadOnlyList<Exercice> Build(
        IReadOnlyCollection<(DateOnly Date, long Cents)> invoices,
        (int Month, int Day) start,
        DateOnly today)
    {
        var current = Containing(today, start);

        var earliest = invoices.Count == 0
            ? current
            : Containing(invoices.Min(invoice => invoice.Date), start);

        var exercices = new List<Exercice>();

        for (var from = earliest.From; from <= current.From; from = from.AddYears(1))
        {
            var to = from.AddYears(1).AddDays(-1);

            exercices.Add(new Exercice(
                Label(from, to),
                from,
                to,
                invoices.Where(invoice => invoice.Date >= from && invoice.Date <= to).Sum(invoice => invoice.Cents),
                from == current.From));
        }

        exercices.Reverse();

        return exercices;
    }
}
