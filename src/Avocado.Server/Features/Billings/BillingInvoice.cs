using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// A facture that was issued <em>elsewhere</em>. Avocado never generates an invoice — she has an
/// invoicing platform for that. This records what was billed so « reste à facturer » can be right.
/// </summary>
public class BillingInvoice
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    public long AmountExclVatCents { get; set; }

    /// <summary>The number the invoicing platform gave it, so the two can be reconciled.</summary>
    public string? ExternalReference { get; set; }

    public bool IsPaid { get; set; }

    public DateOnly? PaidOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
