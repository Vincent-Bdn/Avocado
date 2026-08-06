using Avocado.Server.Features.Billings.Enums;
using Avocado.Server.Features.Billings.ValueObjects;

namespace Avocado.Server.Features.Billings.Endpoints.Dtos;

public sealed record BillingInvoiceItem(
    Guid Id,
    DateOnly Date,
    string? ExternalReference,
    long AmountExclVatCents,
    bool IsPaid,
    DateOnly? PaidOn,
    /// <summary>What the hours on this facture were worth. Zero for a hand-recorded one.</summary>
    long BilledTimeCents,
    /// <summary>Positive = boni, negative = mali. Zero on a hand-recorded facture.</summary>
    long VarianceCents,
    int BilledEntryCount);

/// <param name="Kind">Derived from the stored sign, so the badge and the rendered ± cannot disagree.</param>
/// <param name="AmountCents">Signed, as stored. The UI renders « + 1 200,00 € » / « − 105,00 € ».</param>
public sealed record BillingLedgerItem(
    Guid Id,
    DateOnly Date,
    string Label,
    long AmountCents,
    BillingMovementKind Kind);

/// <summary>
/// « Détail à facturer » — the diligences since the last invoice, which is what gets pasted into the
/// accounting software.
/// </summary>
/// <param name="Since">
/// Date of the last invoice, or null when nothing has been billed yet and everything counts.
/// This is a date heuristic: an invoice records an amount, not which entries it covered.
/// </param>
public sealed record BillingStatement(
    DateOnly? Since,
    int BillableMinutes,
    long BillableAmountCents,
    long DisbursementsToRebillCents,
    long ReceiptsToOffsetCents);

/// <param name="InvoicedOutstandingCents">Billed but not yet paid — what the factures footer states.</param>
public sealed record BillingOverview(
    BillingSummary Summary,
    IReadOnlyList<BillingInvoiceItem> Invoices,
    long InvoicedOutstandingCents,
    IReadOnlyList<BillingLedgerItem> Ledger,
    long ReceiptsCents,
    long DisbursementsCents,
    BillingStatement Statement);
