namespace Avocado.Server.Features.Billings.ValueObjects;

/// <summary>
/// What one dossier has earned, asked for, and received. Computed on read; never stored.
///
/// <para><b>Three questions and two adjustments.</b> It used to be one compound subtraction with two
/// special cases hanging off it, and two lawyers independently reported the same thing: a forfait
/// produced no mali and still left money « à facturer ». The arithmetic below has no special case for
/// the kind of facture, because there is only one kind now.</para>
/// </summary>
/// <param name="WorkedCents">
/// Every billable hour on the dossier at its rate, billed or not. What the work was worth, which is
/// the figure boni and mali are measured against.
/// </param>
/// <param name="UnbilledTimeCents">
/// Billable time no facture has consumed. A facture takes its lines with it, so this is what is still
/// waiting rather than what the dossier has earned since it opened.
/// </param>
/// <param name="InvoicedCents">Factures, HT. An abandon is not one and is absent here.</param>
/// <param name="CollectedCents">
/// Factures marquées payées, plus provisions reçues. The cash side, and the reason a provision no
/// longer belongs in <paramref name="LeftToBillCents"/>: it is money received, not work billed.
/// </param>
/// <param name="OwedCents">
/// <c>facturé − encaissé</c>. Negative means the client is in credit, which is a true statement about
/// a provision taken in advance and no longer a symptom of the formula.
/// </param>
/// <param name="DisbursementsCents">
/// Débours advanced on the client's behalf, still to be recharged. Its own figure: she fronted it, so
/// it is neither work she did nor money she billed.
/// </param>
/// <param name="VarianceCents">
/// Boni (positive) or mali (negative), summed over everything that consumed lines: what she billed
/// less what those lines were worth. A facture that consumed nothing has no gap to report and
/// contributes zero, which is also what a reprise does.
/// </param>
/// <param name="SubcontractedCents">
/// Rétrocessions and other sous-traitance. Absent from <paramref name="LeftToBillCents"/>: the client
/// owes the full fee whoever did the work.
/// </param>
/// <param name="NetCents"><c>facturé − sous-traitance</c>. What the dossier brought the cabinet.</param>
/// <param name="LeftToBillCents">
/// What she may still invoice.
///
/// <para>At the hour, that is the time no facture has taken yet. At a forfait, it is the forfait less
/// what has already gone out, and the hours stop driving it entirely: that is the whole point of
/// agreeing a price, and recording 4 000 € of time against a 3 000 € forfait leaves nothing to bill
/// and a mali of 1 000 €.</para>
/// </summary>
public sealed record BillingSummary(
    long WorkedCents,
    long UnbilledTimeCents,
    int UnbilledMinutes,
    long InvoicedCents,
    long CollectedCents,
    long OwedCents,
    long DisbursementsCents,
    long VarianceCents,
    long SubcontractedCents,
    long NetCents,
    long LeftToBillCents,
    long? FixedFeeCents)
{
    public static BillingSummary Compute(
        long workedCents,
        long unbilledTimeCents,
        int unbilledMinutes,
        long invoicedCents,
        long collectedCents,
        long disbursementsCents,
        long varianceCents,
        long subcontractedCents,
        long legacyDeductionsCents,
        long? fixedFeeCents) =>
        new(
            workedCents,
            unbilledTimeCents,
            unbilledMinutes,
            invoicedCents,
            collectedCents,
            invoicedCents - collectedCents,
            disbursementsCents,
            varianceCents,
            subcontractedCents,
            invoicedCents - subcontractedCents,
            LeftToBill(unbilledTimeCents, invoicedCents, legacyDeductionsCents, fixedFeeCents),
            fixedFeeCents);

    /// <summary>
    /// <paramref name="legacyDeductionsCents"/> is the factures recorded before factures consumed
    /// lines. They subtract although they consumed nothing, because the hours they paid for are still
    /// sitting unbilled and nobody can say after the fact which ones they were. Nothing new is ever
    /// written with that flag; see <see cref="BillingInvoice.DeductsWithoutLines"/>.
    /// </summary>
    internal static long LeftToBill(
        long unbilledTimeCents,
        long invoicedCents,
        long legacyDeductionsCents,
        long? fixedFeeCents) =>
        fixedFeeCents is { } forfait
            ? Math.Max(0, forfait - invoicedCents)
            : unbilledTimeCents - legacyDeductionsCents;
}
