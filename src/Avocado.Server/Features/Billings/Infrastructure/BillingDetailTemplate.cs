using System.Globalization;
using Avocado.Server.Features.Templates.Infrastructure;

namespace Avocado.Server.Features.Billings.Infrastructure;

/// <summary>
/// The fields a cabinet's own « détail de facture » modèle can use.
///
/// <para><b>Why her document rather than one Avocado draws.</b> This page goes to a client, sometimes
/// to a bâtonnier and occasionally to a juge taxateur, and every cabinet's is different: its own
/// letterhead, its own columns, its own closing sentence. A layout built here is Avocado's layout,
/// and the next cabinet wants another. So she uploads the document she already sends, marks the row
/// that repeats with <c>{{ligne.…}}</c>, and everything above and below it stays exactly hers.</para>
///
/// <para>The catalogue is public because the Modèles screen lists it: a placeholder she cannot
/// discover is a placeholder she will not use.</para>
/// </summary>
public static class BillingDetailTemplate
{
    /// <summary>What a modèle must be called to be used for this. Free text elsewhere; matched here.</summary>
    public const string Kind = "Détail de facture";

    /// <summary>The row-level prefix. A table row mentioning any of these is cloned per diligence.</summary>
    public const string RowPrefix = "ligne";

    public static readonly IReadOnlyList<(string Field, string Description)> Catalogue =
    [
        ("facture.numero", "Le numéro de la facture, « 90 »"),
        ("facture.date", "Sa date, « 08/09/2026 »"),
        ("dossier.reference", "La référence du dossier, « 700770-COULEYRE »"),
        ("dossier.nom", "Son nom, « EDF ENR »"),
        ("dossier.rg", "Le n° RG, vide s'il n'y en a pas"),
        ("periode", "« Diligences réalisées entre le 25 août et le 7 septembre 2026 »"),
        ("total", "Le total des diligences, « 4.050 € »"),
        ("heures", "Le temps total, « 20,25 »"),
        ("taux", "Le taux horaire, vide si les lignes n'en partagent pas un seul"),
        ("remise.taux", "La remise, « 30% ». Vide s'il n'y en a pas"),
        ("remise.motif", "Son motif, « pour fidélité client »"),
        ("montant.facture", "Le montant réellement facturé, remise déduite"),
        ("ligne.date", "Dans la ligne qui se répète : la date de la diligence"),
        ("ligne.temps", "Son temps passé en heures décimales, « 0,75 »"),
        ("ligne.observations", "Ce qui a été fait"),
        ("ligne.numero", "Son rang dans le tableau, « 1 », « 2 »…"),
    ];

    /// <summary>The scalars, ready for <see cref="TemplateMerge"/>.</summary>
    public static Dictionary<string, string> Values(
        string? invoiceReference,
        DateOnly invoiceDate,
        string matterReference,
        string matterName,
        string? courtCaseNumber,
        IReadOnlyList<DetailLine> lines,
        int? discountBasisPoints,
        string? discountReason,
        long billedCents)
    {
        var minutes = lines.Sum(line => line.Minutes);
        var rates = lines.Select(line => line.RateCents).Distinct().ToList();

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["facture.numero"] = invoiceReference ?? string.Empty,
            ["facture.date"] = invoiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["dossier.reference"] = matterReference,
            ["dossier.nom"] = matterName,
            ["dossier.rg"] = courtCaseNumber ?? string.Empty,
            ["periode"] = lines.Count == 0
                ? string.Empty
                : BillingDetailDocument.Period(lines.Min(l => l.Date), lines.Max(l => l.Date)),
            ["total"] = BillingDetailDocument.Euros(lines.Sum(line => line.RateCents * line.Minutes / 60)),
            ["heures"] = BillingDetailDocument.Hours(minutes),
            // Empty rather than misleading: with a dérogation among the lines there is no single rate
            // that multiplies out, and printing one would be a figure the client cannot check.
            ["taux"] = rates.Count == 1 ? BillingDetailDocument.Euros(rates[0]) : string.Empty,
            ["remise.taux"] = discountBasisPoints is > 0
                ? (discountBasisPoints.Value / 100m).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + "%"
                : string.Empty,
            ["remise.motif"] = discountReason ?? string.Empty,
            ["montant.facture"] = BillingDetailDocument.Euros(billedCents),
        };
    }

    /// <summary>One dictionary per diligence, for the row that repeats.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(IReadOnlyList<DetailLine> lines) =>
    [
        .. lines.Select((line, index) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ligne.numero"] = (index + 1).ToString(CultureInfo.InvariantCulture),
            ["ligne.date"] = line.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["ligne.temps"] = BillingDetailDocument.Hours(line.Minutes),
            ["ligne.observations"] = line.Task,
        }),
    ];
}
