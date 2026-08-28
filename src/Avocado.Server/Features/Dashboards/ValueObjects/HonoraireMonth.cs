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
/// Every facture dated in that month, whatever work it covers, historical ones included. It is real
/// turnover and belongs on the chart; what it does not belong in is the comparison, which is
/// <see cref="GapCents"/>'s business and not this one's.
/// </param>
/// <param name="HistoricalCents">
/// The part of that brought over from before Avocado. Drawn as its own segment so a month of migrated
/// history does not read as a month of billing without working, and left out of the gap because the
/// hours behind it were never recorded here.
/// </param>
/// <param name="PaidCents">
/// The settled part of what is <em>not</em> historical, by its current state rather than by the date
/// of payment. « Encaissé » here means « facturé ce mois-là, et rentré depuis », which is the question
/// a practice asks about a month it has closed. Historical factures carry their own segment and are
/// not split again inside it: whether a facture from before the migration was settled is a fact about
/// the old system, not about this month.
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
    long HistoricalCents,
    long PaidCents,
    long SubcontractedCents)
{
    /// <summary>Factures for work Avocado could have recorded. The only ones the gap can speak about.</summary>
    public long CurrentCents => InvoicedCents - HistoricalCents;

    public long UnpaidCents => CurrentCents - PaidCents;

    /// <summary>What the month actually brought in, once the confrères are paid. Never below zero.</summary>
    public long NetCents => Math.Max(0, InvoicedCents - SubcontractedCents);

    /// <summary>
    /// The gap the two bars draw, and nothing more than that.
    ///
    /// <para><b>It is not « reste à facturer »</b>, which is what it used to be called. That figure is
    /// a dossier's, it is cumulative, and it nets off provisions; this one is one month of recorded
    /// time against one month of factures. They go negative for entirely ordinary reasons, chiefly
    /// that a facture issued in mars pays for février, and reading « reste à facturer − 7 000 € » off
    /// a chart invites a conclusion about the practice that the number does not support.</para>
    ///
    /// <para>Historical factures are outside it. Their hours were never recorded here, so there is
    /// nothing for them to be compared against, and février 2026 drew 7 000 € facturé against nothing
    /// worked. They stay on the chart, in their own segment: the figure was never the problem, the
    /// comparison was.</para>
    /// </summary>
    public long GapCents => BillableCents - Math.Max(0, CurrentCents - SubcontractedCents);
}

/// <param name="ScaleCents">
/// The top of the shared scale, rounded up to a round figure so the axis reads in whole thousands.
/// Both bars share it, comparing them is the entire point, and the client needs no second pass over
/// the data to find it.
/// </param>
/// <param name="HistoricalCents">
/// How much of the invoiced total was brought over from before Avocado. Shown on the card so the gap
/// it is absent from can be explained rather than merely be smaller than expected.
/// </param>
public sealed record DashboardHonoraires(
    IReadOnlyList<HonoraireMonth> Months,
    long BillableCents,
    long InvoicedCents,
    long HistoricalCents,
    long PaidCents,
    long SubcontractedCents,
    long ScaleCents)
{
    public long CurrentCents => InvoicedCents - HistoricalCents;

    public long UnpaidCents => CurrentCents - PaidCents;

    public long NetCents => InvoicedCents - SubcontractedCents;
}
