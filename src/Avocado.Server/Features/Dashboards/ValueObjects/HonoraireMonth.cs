namespace Avocado.Server.Features.Dashboards.ValueObjects;

/// <summary>
/// One month of « Honoraires facturables et facturés ».
/// </summary>
/// <param name="Month">The first of the month, so the client formats the label it wants.</param>
/// <param name="BillableCents">
/// Time recorded <em>in that month</em>, valued at the entry's rate or the dossier's. Deliberately
/// counted whether or not it has since been billed: the question the chart answers is « ai-je facturé
/// ce que j'ai travaillé ce mois-là », and excluding what was billed would erase the answer.
/// </param>
/// <param name="InvoicedCents">
/// Factures dated in that month, whatever work they cover, <em>except the historical ones</em>.
///
/// <para>Excluded for the same reason as sous-traitance below: the facturable bar is time recorded in
/// Avocado, and a facture repris de Gestisoft covers hours that were never recorded here. Leaving them
/// in drew a month with 7 000 € invoiced against nothing worked, and called the gap « reste à
/// facturer − 7 000 € ». There is no month of hers where that comparison could have meant
/// anything.</para>
/// </param>
/// <param name="PaidCents">
/// The part of those factures marked as settled, by their current state rather than by the date of
/// payment. « Encaissé » here means « facturé ce mois-là, et rentré depuis », which is the question a
/// practice asks about a month it has closed.
/// </param>
/// <param name="SubcontractedCents">
/// Rétrocessions and other sous-traitance recorded that month. Subtracted from the invoiced figure
/// on the chart, because the two bars would otherwise not be comparable: the facturable bar is her
/// own time and contains none of the confrère's hours, while the invoiced bar contains what she
/// charged for them. Netting it off is what makes the gap mean « ai-je facturé ce que j'ai
/// travaillé » rather than « ai-je facturé plus que je n'ai travaillé moi-même ».
/// </param>
public sealed record HonoraireMonth(
    DateOnly Month,
    long BillableCents,
    long InvoicedCents,
    long PaidCents,
    long SubcontractedCents)
{
    public long UnpaidCents => InvoicedCents - PaidCents;

    /// <summary>What the month actually brought in, once the confrères are paid. Never below zero.</summary>
    public long NetCents => Math.Max(0, InvoicedCents - SubcontractedCents);

    /// <summary>
    /// The gap the two bars draw, and nothing more than that.
    ///
    /// <para><b>It is not « reste à facturer »</b>, which is what it used to be called. That figure is
    /// a dossier's, it is cumulative, and it nets off provisions; this one is one month of recorded
    /// time against one month of factures. They go negative for entirely ordinary reasons, chiefly
    /// that a facture issued in March pays for February, and reading « reste à facturer − 7 000 € »
    /// off a chart invites a conclusion about the practice that the number does not support.</para>
    /// </summary>
    public long GapCents => BillableCents - NetCents;
}

/// <param name="ScaleCents">
/// The top of the shared scale, rounded up to a round figure so the axis reads in whole thousands.
/// Both bars share it, comparing them is the entire point, and the client needs no second pass over
/// the data to find it.
/// </param>
/// <param name="HistoricalCents">
/// What was set aside: factures brought over from before Avocado, dated inside the window, whose work
/// was never recorded here. Reported rather than simply dropped, because a chart quietly missing
/// 8 974 € of real turnover is the same failure as one quietly inventing a gap.
/// </param>
public sealed record DashboardHonoraires(
    IReadOnlyList<HonoraireMonth> Months,
    long BillableCents,
    long InvoicedCents,
    long PaidCents,
    long SubcontractedCents,
    long HistoricalCents,
    long ScaleCents)
{
    public long UnpaidCents => InvoicedCents - PaidCents;

    public long NetCents => InvoicedCents - SubcontractedCents;
}
