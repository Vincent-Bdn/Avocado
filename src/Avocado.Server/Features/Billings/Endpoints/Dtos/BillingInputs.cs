using Avocado.Server.Features.Billings.Enums;

namespace Avocado.Server.Features.Billings.Endpoints.Dtos;

/// <param name="AmountExclVatCents">
/// Hors taxes. Avocado records what was billed elsewhere and never computes VAT.
/// </param>
/// <param name="IsHistorical">
/// A facture whose work was never recorded here: issued before Avocado or in another system. Drawn
/// apart on the chart, outside the comparison.
///
/// <para><b>No longer offered in the window.</b> It decides no arithmetic — a facture consumes the
/// lines it actually took, so one that took none subtracts nothing — and asking her to classify every
/// facture she records as « antérieure » or not was a question with no consequence. Still written by
/// the Gestisoft import, which is the one place the answer is known, and still read everywhere so the
/// factures already carrying it keep their segment.</para>
/// </param>
/// <param name="TimeEntryIds">
/// The lines this facture settles. Honoured on creation; ignored when correcting an existing facture,
/// deliberately, since fixing a typo in an amount must not silently consume hours.
/// </param>
public sealed record BillingInvoiceInput(
    DateOnly Date,
    long AmountExclVatCents,
    string? ExternalReference,
    bool IsPaid = false,
    DateOnly? PaidOn = null,
    bool IsHistorical = false,
    int? DiscountBasisPoints = null,
    string? DiscountReason = null,
    IReadOnlyList<Guid>? TimeEntryIds = null)
{
    public string? Validate() => this switch
    {
        { AmountExclVatCents: <= 0 } => "Le montant doit être positif.",
        { IsPaid: false, PaidOn: not null } => "Une facture non réglée ne peut pas avoir de date de règlement.",
        { DiscountBasisPoints: < 0 or > 10_000 } => "La remise doit être comprise entre 0 et 100%.",
        _ => null,
    };
}

/// <param name="Kind">Chosen before the amount. Determines the stored sign.</param>
/// <param name="AmountCents">
/// Always positive, the amount received, or the amount advanced. The client never sends a sign and
/// the server refuses one, so a débours cannot become a receipt through a typo.
///
/// <para><b>Hors taxes</b>, like every other amount in Avocado. It matters here more than anywhere
/// else: a provision reçue is added to the factures marked paid to make « encaissé », so a provision
/// typed TTC would add VAT to a figure made of HT and produce a number that is wrong, plausible, and
/// noticed by nobody. The window says « € HT » on the field for that reason.</para>
/// </param>
public sealed record BillingLedgerInput(
    BillingMovementKind Kind,
    DateOnly Date,
    long AmountCents,
    string Label)
{
    public string? Validate() => this switch
    {
        { AmountCents: <= 0 } =>
            "Le montant doit être positif : la nature du mouvement détermine le signe.",
        { Label: var label } when string.IsNullOrWhiteSpace(label) =>
            "Le libellé est obligatoire.",
        _ => null,
    };

    /// <summary>Positive for an encaissement, negative for a débours.</summary>
    public long SignedAmountCents => Kind == BillingMovementKind.Receipt ? AmountCents : -AmountCents;
}
