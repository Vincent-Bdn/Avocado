using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// Money that moved <em>without</em> an invoice: a provision received, a débours advanced, a
/// correction. Signed — <b>positive = received from the client, negative = advanced on the matter</b>.
/// <para>
/// The boundary rule, and the only place this model can silently produce a wrong number: an amount is
/// either a <see cref="BillingInvoice"/> or a <see cref="BillingLedgerEntry"/>, never both. A
/// provision that was itself invoiced and also entered here would be counted twice.
/// </para>
/// <para>
/// The UI must never expose the sign as a raw field. Two buttons — <i>Encaissement</i> and
/// <i>Débours</i> — set it, because a débours typed as a positive number makes every balance wrong
/// while still looking entirely plausible.
/// </para>
/// </summary>
public class BillingLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    public long AmountCents { get; set; }

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
