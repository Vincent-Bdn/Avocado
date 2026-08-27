using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Templates.Infrastructure;

namespace Avocado.Server.Tests.Templates;

/// <summary>
/// The values a template drops into a letter.
///
/// <para>These exist because the whole thing threw. Avocado publishes with InvariantGlobalization,
/// where the invariant culture is the only one there is, and every field here was formatted through
/// <c>CultureInfo.GetCultureInfo("fr-FR")</c>, which does not fall back to it, it throws. Nothing
/// caught it in development, where the machine has a French locale and the culture is real.</para>
/// </summary>
public class TemplateFieldsTests
{
    private static readonly Matter Matter = new()
    {
        Reference = "2026-0114",
        Name = "Cession du fonds de commerce",
        OpenedOn = new DateOnly(2025, 11, 4),
        HourlyRateCents = 24_000,
    };

    private static readonly Contact Client = new()
    {
        Type = ContactType.Organisation,
        LegalName = "SAS Berthier Négoce",
    };

    /// <summary>The one that was broken, and it broke every field at once.</summary>
    [Fact]
    public void FillsAletterWithoutAskingForACultureTheRuntimeDoesNotHave()
    {
        var fields = TemplateFields.For(Matter, Client, new DateOnly(2026, 8, 6));

        Assert.Equal("2026-0114", fields["dossier.reference"]);
        Assert.Equal("04/11/2025", fields["dossier.ouvertLe"]);
        Assert.Equal("SAS Berthier Négoce", fields["client.nom"]);
    }

    [Fact]
    public void WritesTheDateOutInFrenchForProse() =>
        Assert.Equal("6 août 2026", TemplateFields.For(Matter, Client, new DateOnly(2026, 8, 6))["date.aujourdhui"]);

    /// <summary>In French the first of the month is ordinal and no other day is.</summary>
    [Fact]
    public void WritesTheFirstOfTheMonthAsAnOrdinal() =>
        Assert.Equal("1er septembre 2026", TemplateFields.For(Matter, Client, new DateOnly(2026, 9, 1))["date.aujourdhui"]);

    [Fact]
    public void WritesTheDateInFiguresForAHeader() =>
        Assert.Equal("06/08/2026", TemplateFields.For(Matter, Client, new DateOnly(2026, 8, 6))["date.aujourdhuiCourt"]);

    /// <summary>
    /// Non-breaking spaces, both of them, because a letter that wraps between the figure and the euro
    /// sign is the sort of thing a client notices.
    /// </summary>
    [Theory]
    [InlineData(24_000, "240,00 €")]
    [InlineData(0, "0,00 €")]
    [InlineData(5, "0,05 €")]
    [InlineData(123_456, "1 234,56 €")]
    [InlineData(123_456_789, "1 234 567,89 €")]
    [InlineData(-123_456, "-1 234,56 €")]
    public void WritesMoneyTheWayAFrenchLetterWritesIt(long cents, string expected) =>
        Assert.Equal(expected, TemplateFields.Euros(cents));

    /// <summary>
    /// The catalogue is what the UI shows her as an example of each field, so an example that no longer
    /// matches what the field produces is a promise the letter breaks.
    /// </summary>
    [Fact]
    public void ShowsAnExampleRateThatMatchesWhatItWouldActuallyWrite() =>
        Assert.Equal(
            TemplateFields.Euros(24_000),
            TemplateFields.Catalogue.Single(entry => entry.Field == "dossier.tauxHoraire").Description);

    [Fact]
    public void LeavesEveryFieldOfAnAbsentClientEmptyRatherThanNull()
    {
        var fields = TemplateFields.For(Matter, client: null, new DateOnly(2026, 8, 6));

        Assert.Equal(string.Empty, fields["client.nom"]);
        Assert.Equal(string.Empty, fields["client.adresse"]);
    }
}
