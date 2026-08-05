namespace Avocado.Server.Features.Billings.ValueObjects;

/// <summary>
/// « Détail à facturer » for one matter — the figures to paste into whatever issues the invoice, not
/// an invoice. Computed on read; never stored.
/// </summary>
/// <param name="BillableTimeCents">Billable minutes valued at the matter rate, or each entry's override.</param>
/// <param name="LedgerCents">Signed sum of the ledger: provisions received less débours advanced.</param>
/// <param name="LeftToBillCents">
/// <c>billable time − ledger − already invoiced</c>. May be negative, meaning the client is in credit,
/// and that has to be shown as such rather than clamped to zero.
/// </param>
public sealed record BillingSummary(
    long BillableTimeCents,
    int BillableMinutes,
    long LedgerCents,
    long InvoicedCents,
    long LeftToBillCents)
{
    public static BillingSummary Compute(
        long billableTimeCents,
        int billableMinutes,
        long ledgerCents,
        long invoicedCents) =>
        new(
            billableTimeCents,
            billableMinutes,
            ledgerCents,
            invoicedCents,
            billableTimeCents - ledgerCents - invoicedCents);
}
