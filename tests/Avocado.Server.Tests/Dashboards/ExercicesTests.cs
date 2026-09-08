using Avocado.Server.Features.Dashboards.ValueObjects;
using Avocado.Server.Features.Settings;

namespace Avocado.Server.Tests.Dashboards;

/// <summary>
/// Le chiffre d'affaires par exercice. She reads these figures to know what she has earned, so a
/// facture landing in the wrong year is not a display bug.
/// </summary>
public class ExercicesTests
{
    private static readonly (int Month, int Day) September = (9, 1);

    private static (DateOnly Date, long Cents) Facture(int year, int month, int day, long cents) =>
        (new DateOnly(year, month, day), cents);

    [Fact]
    public void PutsTheFirstOfSeptemberAtTheStartOfItsOwnExercice()
    {
        var (from, to) = Exercices.Containing(new DateOnly(2026, 9, 1), September);

        Assert.Equal(new DateOnly(2026, 9, 1), from);
        Assert.Equal(new DateOnly(2027, 8, 31), to);
    }

    [Fact]
    public void PutsTheDayBeforeInThePreviousOne()
    {
        // The 31st of August is the last day of the exercice that opened the previous September, and
        // a facture dated then belongs to the year that is closing, not the one about to open.
        var (from, to) = Exercices.Containing(new DateOnly(2026, 8, 31), September);

        Assert.Equal(new DateOnly(2025, 9, 1), from);
        Assert.Equal(new DateOnly(2026, 8, 31), to);
    }

    [Fact]
    public void NamesAStraddlingYearByBothOfThem()
    {
        Assert.Equal("2026 - 2027", Exercices.Label(new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        Assert.Equal("2026", Exercices.Label(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void CountsEachFactureInExactlyOneExercice()
    {
        var invoices = new[]
        {
            Facture(2026, 9, 15, 400_000),    // First days of the new exercice.
            Facture(2026, 8, 20, 250_000),    // Last days of the old one.
            Facture(2027, 8, 31, 100_000),    // The very last day.
            Facture(2027, 9, 1, 50_000),      // The very first day of the next.
        };

        var built = Exercices.Build(invoices, September, new DateOnly(2027, 9, 8));

        var byLabel = built.ToDictionary(exercice => exercice.Label, exercice => exercice.InvoicedCents);

        Assert.Equal(250_000, byLabel["2025 - 2026"]);
        Assert.Equal(500_000, byLabel["2026 - 2027"]);
        Assert.Equal(50_000, byLabel["2027 - 2028"]);
        Assert.Equal(invoices.Sum(invoice => invoice.Cents), byLabel.Values.Sum());
    }

    [Fact]
    public void PutsTheRunningExerciceFirstAndSaysWhichItIs()
    {
        var built = Exercices.Build(
            [Facture(2026, 9, 15, 400_000)],
            September,
            new DateOnly(2027, 3, 8));

        Assert.Equal("2026 - 2027", built[0].Label);
        Assert.True(built[0].IsCurrent);
        Assert.All(built.Skip(1), exercice => Assert.False(exercice.IsCurrent));
    }

    [Fact]
    public void KeepsAQuietYearRatherThanSkippingIt()
    {
        // Nothing invoiced in 2027-2028. A missing row would read as a lost year; a zero reads as a
        // year that was quiet, which is what it was.
        var built = Exercices.Build(
            [Facture(2026, 10, 1, 400_000), Facture(2028, 10, 1, 900_000)],
            September,
            new DateOnly(2028, 12, 1));

        Assert.Equal(["2028 - 2029", "2027 - 2028", "2026 - 2027"], built.Select(e => e.Label));
        Assert.Equal(0, built.Single(e => e.Label == "2027 - 2028").InvoicedCents);
    }

    [Fact]
    public void ShowsTheCurrentExerciceEvenBeforeTheFirstFacture()
    {
        var built = Exercices.Build([], September, new DateOnly(2026, 9, 8));

        Assert.Single(built);
        Assert.Equal("2026 - 2027", built[0].Label);
        Assert.Equal(0, built[0].InvoicedCents);
    }

    [Fact]
    public void ReadsHerOwnStartDate()
    {
        var settings = new Dictionary<string, string> { [PracticeSettingKeys.ExerciceStart] = "09-01" };

        Assert.Equal((9, 1), Exercices.StartFrom(settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("septembre")]
    [InlineData("13-01")]
    [InlineData("02-30")]
    [InlineData("02-29")]   // Refused on purpose: three years in four it would not exist.
    public void FallsBackToTheCalendarYearRatherThanGuessing(string written)
    {
        var settings = new Dictionary<string, string> { [PracticeSettingKeys.ExerciceStart] = written };

        Assert.Equal((1, 1), Exercices.StartFrom(settings));
    }
}
