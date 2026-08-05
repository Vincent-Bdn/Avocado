using Avocado.Server.Features.Billings.Enums;

namespace Avocado.Server.Features.Billings.Endpoints.Dtos;

/// <param name="AmountExclVatCents">
/// Hors taxes. Avocado records what was billed elsewhere and never computes VAT.
/// </param>
public sealed record BillingInvoiceInput(
    DateOnly Date,
    long AmountExclVatCents,
    string? ExternalReference,
    bool IsPaid = false,
    DateOnly? PaidOn = null)
{
    public string? Validate() => this switch
    {
        { AmountExclVatCents: <= 0 } => "Le montant doit être positif.",
        { IsPaid: false, PaidOn: not null } => "Une facture non réglée ne peut pas avoir de date de règlement.",
        _ => null,
    };
}

/// <param name="Kind">Chosen before the amount. Determines the stored sign.</param>
/// <param name="AmountCents">
/// Always positive — the amount received, or the amount advanced. The client never sends a sign and
/// the server refuses one, so a débours cannot become a receipt through a typo.
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
