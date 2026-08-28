namespace Avocado.Server.Features.Billings;

/// <summary>
/// Which factures reduce « reste à facturer », in one place because three screens ask.
///
/// <para>The fiche computes it, the accueil computes it again for its « temps saisi non facturé », and
/// the honoraires chart draws it as a gap. <see cref="BillingSummaryQuery"/> already carried a warning
/// that a second implementation would eventually disagree with the first, and by the time anyone
/// looked there were three, disagreeing in two different ways: the accueil subtracted every facture
/// rather than only the hand-recorded ones, and counted hours already attached to one.</para>
///
/// <para>So the rule lives here and nowhere else.</para>
/// </summary>
public static class BillingRules
{
    /// <summary>
    /// Whether this facture's amount comes off what is still to bill.
    ///
    /// <para><b>A facture built from selected hours does not</b>: those hours were marked as billed
    /// when it was issued, so they have already left the unbilled total and taking the amount off as
    /// well would count the same work twice.</para>
    ///
    /// <para><b>A historical facture does not either</b>: its hours were never recorded here, so there
    /// is nothing for it to have consumed and subtracting it removes money that was never counted.
    /// COULEYRE came over with 8 974 € of them and read « reste à facturer − 8 974 € ».</para>
    ///
    /// <para>What is left is a facture she recorded by hand for work that is still sitting in the
    /// unbilled time, and that one has to be subtracted or the same work is billed twice.</para>
    /// </summary>
    public static bool ReducesLeftToBill(long billedTimeCents, bool isHistorical) =>
        billedTimeCents == 0 && !isHistorical;

    /// <inheritdoc cref="ReducesLeftToBill(long, bool)"/>
    public static bool ReducesLeftToBill(BillingInvoice invoice) =>
        ReducesLeftToBill(invoice.BilledTimeCents, invoice.IsHistorical);
}
