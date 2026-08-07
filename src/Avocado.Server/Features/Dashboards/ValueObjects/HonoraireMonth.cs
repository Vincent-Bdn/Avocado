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
/// <param name="InvoicedCents">Factures dated in that month, whatever work they cover.</param>
/// <param name="PaidCents">
/// The part of those factures marked as settled, by their current state rather than by the date of
/// payment. « Encaissé » here means « facturé ce mois-là, et rentré depuis », which is the question a
/// practice asks about a month it has closed.
/// </param>
public sealed record HonoraireMonth(
    DateOnly Month,
    long BillableCents,
    long InvoicedCents,
    long PaidCents)
{
    public long UnpaidCents => InvoicedCents - PaidCents;

    /// <summary>The gap the two bars draw. Negative means that month billed more than it worked.</summary>
    public long LeftToBillCents => BillableCents - InvoicedCents;
}

/// <param name="ScaleCents">
/// The top of the shared scale, rounded up to a round figure so the axis reads in whole thousands.
/// Both bars share it — comparing them is the entire point — and the client needs no second pass over
/// the data to find it.
/// </param>
public sealed record DashboardHonoraires(
    IReadOnlyList<HonoraireMonth> Months,
    long BillableCents,
    long InvoicedCents,
    long PaidCents,
    long ScaleCents)
{
    public long UnpaidCents => InvoicedCents - PaidCents;
}
