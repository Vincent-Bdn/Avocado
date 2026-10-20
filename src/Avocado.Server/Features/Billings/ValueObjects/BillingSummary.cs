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
///
/// <para><b>A forfait answers this differently</b>, and used not to answer it at all. Once the agreed
/// price is entirely billed, the hours stop being billable at all, so the arbitrage is no longer the
/// sum of what each facture decided: it is the price against the whole of the work, and it cannot be
/// better than that. Without this rule a dossier at 3 000 € with 4 000 € of recorded time reported no
/// mali whatever she did, which is the « le mali n'apparaît pas sur les dossiers » that two lawyers
/// reported and that the summary's own documentation already claimed was handled. See
/// <see cref="Variance"/>.</para>
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
    /// <summary>
    /// One dossier's figures, from its rows.
    ///
    /// <para>Which rows count is the part that has gone wrong, never the sums: an abandon is not
    /// turnover, a facture that took no lines has no gap to report, a provision is cash and not work
    /// billed. All of that is here, in one place, reachable without a database.</para>
    /// </summary>
    public static BillingSummary From(BillingInputs inputs)
    {
        long Value(BillingTimeLine line) =>
            (line.RateCentsOverride ?? inputs.HourlyRateCents) * line.DurationMinutes / 60;

        var unbilled = inputs.Time.Where(line => !line.IsBilled).ToList();

        // An abandon exists to take lines out of the unbilled total and to say so as a mali. It is
        // not turnover and appears in no figure that counts money coming in.
        var billed = inputs.Factures.Where(facture => !facture.IsAbandon).ToList();

        // Signed: received from the client is positive, advanced on his behalf negative. Two
        // different facts, no longer added together into one that means neither.
        var provisionsCents = inputs.LedgerCents.Where(amount => amount > 0).Sum();

        return Compute(
            inputs.Time.Sum(Value),
            unbilled.Sum(Value),
            unbilled.Sum(line => line.DurationMinutes),
            billed.Sum(facture => facture.AmountExclVatCents),
            billed.Where(facture => facture.IsPaid).Sum(facture => facture.AmountExclVatCents)
                + provisionsCents,
            -inputs.LedgerCents.Where(amount => amount < 0).Sum(),
            // What she billed, less what the lines she billed it for were worth. One that consumed
            // no lines decided nothing and reports nothing.
            inputs.Factures
                .Where(facture => facture.BilledTimeCents != 0)
                .Sum(facture => facture.AmountExclVatCents - facture.BilledTimeCents),
            inputs.SubcontractedCents,
            billed
                .Where(facture => facture.DeductsWithoutLines)
                .Sum(facture => facture.AmountExclVatCents),
            inputs.FixedFeeCents);
    }

    /// <summary>
    /// The downstream arithmetic, once the rows have been reduced to figures. Internal rather than
    /// private so BillingSummaryTests can state each rule as one line of numbers; nothing outside
    /// this assembly reaches it, and <see cref="From"/> is the only way in.
    /// </summary>
    internal static BillingSummary Compute(
        long workedCents,
        long unbilledTimeCents,
        int unbilledMinutes,
        long invoicedCents,
        long collectedCents,
        long disbursementsCents,
        long varianceCents,
        long subcontractedCents,
        long legacyDeductionsCents,
        long? fixedFeeCents)
    {
        var leftToBillCents = LeftToBill(
            unbilledTimeCents, invoicedCents, legacyDeductionsCents, fixedFeeCents);

        return new(
            workedCents,
            unbilledTimeCents,
            unbilledMinutes,
            invoicedCents,
            collectedCents,
            invoicedCents - collectedCents,
            disbursementsCents,
            Variance(varianceCents, workedCents, invoicedCents, fixedFeeCents, leftToBillCents),
            subcontractedCents,
            invoicedCents - subcontractedCents,
            leftToBillCents,
            fixedFeeCents);
    }

    /// <summary>
    /// The arbitrage on price, with the forfait's own answer applied.
    ///
    /// <para>At the hour it is simply what the factures decided, one at a time: each one billed an
    /// amount against lines of a known value, and the gaps add up. Hours nobody has billed yet are
    /// not an arbitrage, they are work waiting.</para>
    ///
    /// <para><b>At a forfait, once there is nothing left to bill, it is the price against the
    /// work.</b> The hours can never be invoiced now, so « she might still bill them » has stopped
    /// being true, and every hour past the forfait is a mali she granted by agreeing the price. This
    /// is the figure she states herself: « je négocie 3 000, je rentre 4 000 de temps passé, donc
    /// 1 000 de mali ».</para>
    ///
    /// <para><b>The lower of the two</b>, never the forfait's figure alone. A facture that consumed no
    /// lines settled nothing and cannot be compared to hours, so a dossier carrying one must not
    /// report a boni it did not earn: with 3 000 € typed in by hand and no work recorded,
    /// <c>facturé − travaillé</c> is + 3 000 and means nothing. Taking the lower keeps the honest
    /// answer in both directions, and the two coincide exactly when every hour has been consumed,
    /// which is the normal case.</para>
    ///
    /// <para><b>Only once the forfait is fully billed.</b> Half a forfait billed with hours still to
    /// come is not an arbitrage yet: she can still invoice the rest, and declaring the mali early
    /// would announce a loss she has not taken.</para>
    /// </summary>
    internal static long Variance(
        long factureVarianceCents,
        long workedCents,
        long invoicedCents,
        long? fixedFeeCents,
        long leftToBillCents) =>
        fixedFeeCents is not null && leftToBillCents == 0
            ? Math.Min(factureVarianceCents, invoicedCents - workedCents)
            : factureVarianceCents;

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
