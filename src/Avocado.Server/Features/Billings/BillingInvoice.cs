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

    /// <summary>
    /// What the hours attached to this facture were worth at the dossier's rate, recorded at the
    /// moment it was issued.
    /// <para>
    /// The difference with <see cref="AmountExclVatCents"/> is the <b>boni</b> or the <b>mali</b>:
    /// billing 6 000 € for 7 200 € of recorded time is a 1 200 € mali, deliberately granted, and the
    /// reverse is a boni. It is stored rather than recomputed because the rate, the entries and the
    /// corrections all move afterwards, and the figure she wants is what the arbitrage actually was
    /// on the day she made it.
    /// </para>
    /// </summary>
    public long BilledTimeCents { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Positive = boni (billed above the recorded time), negative = mali.</summary>
    public long VarianceCents => AmountExclVatCents - BilledTimeCents;
}
