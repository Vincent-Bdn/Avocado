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
        ("dossier.tauxHoraire", "240,00 €"),
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
        var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dossier.reference"] = matter.Reference,
            ["dossier.nom"] = matter.Name,
            ["dossier.description"] = matter.Description ?? string.Empty,
            ["dossier.nature"] = matter.Classification ?? string.Empty,
            ["dossier.juridiction"] = matter.Court ?? string.Empty,
            ["dossier.rg"] = matter.CourtCaseNumber ?? string.Empty,
            ["dossier.ouvertLe"] = matter.OpenedOn.ToString("dd/MM/yyyy", culture),
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
            ["date.aujourdhui"] = today.ToString("d MMMM yyyy", culture),
            ["date.aujourdhuiCourt"] = today.ToString("dd/MM/yyyy", culture),
        };
    }

    /// <summary>Non-breaking space before the euro sign, as French typography requires.</summary>
    public static string Euros(long cents) =>
        string.Create(
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
            $"{cents / 100m:N2} €");

    public static bool IsOrganisation(Contact? contact) => contact?.Type == ContactType.Organisation;
}
