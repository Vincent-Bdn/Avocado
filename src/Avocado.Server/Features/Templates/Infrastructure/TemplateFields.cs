using System.Globalization;
using System.Text;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Templates.Infrastructure;

/// <summary>
/// What a template can ask for, in French, spelled the way she would write it into a letter.
/// <para>
/// The list is deliberately flat and small. A template language with loops and conditions is a
/// programming language, and the person writing these is a lawyer in Word: everything here is a value
/// she can drop in a sentence, and anything more elaborate belongs in the letter she writes around it.
/// </para>
/// </summary>
public static class TemplateFields
{
    /// <summary>Shown in the UI so she knows what she can type, with an example of each.</summary>
    public static readonly IReadOnlyList<(string Field, string Description)> Catalogue =
    [
        ("dossier.reference", "2026-0114"),
        ("dossier.nom", "Cession du fonds de commerce, rue Duquesne"),
        ("dossier.description", "La description du dossier"),
        ("dossier.nature", "Contentieux"),
        ("dossier.juridiction", "TC Lyon"),
        ("dossier.rg", "24/01187"),
        ("dossier.ouvertLe", "04/11/2025"),
        ("dossier.tauxHoraire", "240,00 €"),
        ("client.nom", "SAS Berthier Négoce"),
        ("client.civilite", "Mme"),
        ("client.adresse", "14 rue Duquesne, 69003 Lyon"),
        ("client.siren", "842 671 093"),
        ("client.formeJuridique", "SAS"),
        ("client.courriel", "contact@berthier.fr"),
        ("client.telephone", "04 72 00 00 00"),
        ("date.aujourdhui", "6 août 2026"),
        ("date.aujourdhuiCourt", "06/08/2026"),
    ];

    public static Dictionary<string, string> For(Matter matter, Contact? client, DateOnly today)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dossier.reference"] = matter.Reference,
            ["dossier.nom"] = matter.Name,
            ["dossier.description"] = matter.Description ?? string.Empty,
            ["dossier.nature"] = matter.Classification ?? string.Empty,
            ["dossier.juridiction"] = matter.Court ?? string.Empty,
            ["dossier.rg"] = matter.CourtCaseNumber ?? string.Empty,
            ["dossier.ouvertLe"] = Short(matter.OpenedOn),
            ["dossier.tauxHoraire"] = Euros(matter.HourlyRateCents),
            ["client.nom"] = client?.DisplayName ?? string.Empty,
            ["client.civilite"] = client?.Civility ?? string.Empty,
            ["client.adresse"] = client?.Address ?? string.Empty,
            ["client.siren"] = client?.Siren ?? string.Empty,
            ["client.formeJuridique"] = client?.LegalForm ?? string.Empty,
            ["client.courriel"] = client?.Email ?? string.Empty,
            ["client.telephone"] = client?.Phone ?? string.Empty,
            // « 6 août 2026 » in prose, JJ/MM/AAAA in a reference line: both, because a letter uses
            // one and a header uses the other.
            ["date.aujourdhui"] = Long(today),
            ["date.aujourdhuiCourt"] = Short(today),
        };
    }

    /// <summary>
    /// The month names, written out here rather than asked of a French culture, and the same goes for
    /// the two date formats and for <see cref="Euros"/> below.
    ///
    /// <para>Avocado publishes with InvariantGlobalization, where the invariant culture is the only one
    /// that exists and <c>GetCultureInfo("fr-FR")</c> throws CultureNotFoundException rather than
    /// falling back to it. Every template merge was doing exactly that. It cannot show up on a
    /// development machine with a French locale, only in the built application, which is the only place
    /// she uses it.</para>
    /// </summary>
    private static readonly string[] Months =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    /// <summary>« 6 août 2026 », and « 1er septembre » when it falls on the first.</summary>
    private static string Long(DateOnly date) =>
        $"{(date.Day == 1 ? "1er" : date.Day.ToString(CultureInfo.InvariantCulture))} {Months[date.Month - 1]} {date.Year}";

    /// <summary>« 06/08/2026 ».</summary>
    private static string Short(DateOnly date) => $"{date.Day:00}/{date.Month:00}/{date.Year:0000}";

    /// <summary>
    /// « 1 234,56 € », with a non-breaking space before the euro sign and between the thousands, as
    /// French typography requires and as no culture available here would produce.
    /// </summary>
    public static string Euros(long cents)
    {
        var absolute = Math.Abs(cents);
        var units = (absolute / 100).ToString(CultureInfo.InvariantCulture);
        var grouped = new StringBuilder(cents < 0 ? "-" : string.Empty);

        for (var index = 0; index < units.Length; index++)
        {
            if (index > 0 && (units.Length - index) % 3 == 0)
            {
                grouped.Append(' ');
            }

            grouped.Append(units[index]);
        }

        return $"{grouped},{absolute % 100:00} €";
    }

    public static bool IsOrganisation(Contact? contact) => contact?.Type == ContactType.Organisation;
}
