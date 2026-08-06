namespace Avocado.Server.Features.Billings.ValueObjects;

/// <summary>
/// « Détail à facturer » for one matter — the figures to paste into whatever issues the invoice, not
/// an invoice. Computed on read; never stored.
/// </summary>
/// <param name="BillableTimeCents">
/// Billable time <em>not yet attached to a facture</em>, valued at the matter rate or each entry's
/// override. Hours billed on a facture stop counting: what she needs to know is what she has earned
/// since the last one, not what the dossier has earned since it opened.
/// </param>
/// <param name="LedgerCents">Signed sum of the ledger: provisions received less débours advanced.</param>
/// <param name="InvoicedCents">Everything invoiced on the dossier, shown as its own figure.</param>
/// <param name="ManualInvoicedCents">
/// The part of that which was recorded by hand rather than established from selected hours. Only
/// this part is subtracted: a facture built from time already consumed its own lines, and taking its
/// amount off again would count the same work twice.
/// </param>
/// <param name="VarianceCents">
/// Boni (positive) or mali (negative) accumulated on this dossier: the sum of what was billed less
/// what the billed hours were worth. The KPI a practice actually watches.
/// </param>
/// <param name="LeftToBillCents">
/// <c>unbilled time − ledger − manual invoices</c>. May be negative, meaning the client is in credit,
/// and that has to be shown as such rather than clamped to zero.
/// </param>
public sealed record BillingSummary(
    long BillableTimeCents,
    int BillableMinutes,
    long LedgerCents,
    long InvoicedCents,
    long ManualInvoicedCents,
    long VarianceCents,
    long LeftToBillCents)
{
    public static BillingSummary Compute(
        long unbilledTimeCents,
        int unbilledMinutes,
        long ledgerCents,
        long invoicedCents,
        long manualInvoicedCents,
        long varianceCents) =>
        new(
            unbilledTimeCents,
            unbilledMinutes,
            ledgerCents,
            invoicedCents,
            manualInvoicedCents,
            varianceCents,
            unbilledTimeCents - ledgerCents - manualInvoicedCents);
}
