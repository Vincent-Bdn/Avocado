using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// What the dossier cost the cabinet: a rétrocession d'honoraires to a confrère, a sous-traitance, a
/// traduction. Money the practice spends on the work, out of the fee it charges for it.
///
/// <para><b>Why this is not an invoice with a negative amount.</b> A <see cref="BillingInvoice"/> is
/// a facture <em>she issued</em>; this is one <em>issued to her</em>. Every attribute points the other
/// way, who the counterparty is, who owes whom, whether <c>IsPaid</c> means the client settled or she
/// did. <c>BilledTimeCents</c> and the boni/mali would be meaningless, since a confrère's invoice
/// covers none of her hours. And the détail de facturation that goes to the client is filtered from
/// the invoice table: a negative row in there would depend on a sign test to stay out of a document
/// that leaves the building.</para>
///
/// <para><b>Why this is not a débours either.</b> A débours is advanced <em>for the client's
/// account</em> and re-billed at cost, greffe, huissier, expertise, so it increases what remains to
/// be billed. A rétrocession does not: the client still owes the full fee, and the cost comes out of
/// the cabinet's own margin. Putting it in the ledger would inflate « reste à facturer » by the exact
/// amount she is going to pay away.</para>
///
/// <para>The case where a subcontractor genuinely is re-billed at cost to the client is a débours and
/// belongs in <see cref="BillingLedgerEntry"/>, the distinction is who bears it, not who did the
/// work.</para>
/// </summary>
public class BillingCost
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>
    /// « Rétrocession d'honoraires », « Sous-traitance », « Traduction », « Expertise privée ». Free
    /// text and optional: the UI suggests the usual answers without imposing them, and a practice
    /// that meets a kind of cost nobody thought of never has to wait for a release to record it.
    /// </summary>
    public string? Kind { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Who was paid, when they are in the carnet, a confrère very often already is. Nullable because
    /// a one-off translator is not worth a fiche, and detaching a tiers must not delete the cost.
    /// </summary>
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    /// <summary>Always positive. The direction is what the type means, never what the sign says.</summary>
    public long AmountExclVatCents { get; set; }

    /// <summary>Their invoice number, so the two sides can be reconciled.</summary>
    public string? ExternalReference { get; set; }

    /// <summary>Whether <em>she</em> has settled it. The mirror of <see cref="BillingInvoice.IsPaid"/>.</summary>
    public bool IsPaid { get; set; }

    public DateOnly? PaidOn { get; set; }

    /// <summary>
    /// The facture this cost was incurred against, when it is known. Optional, because the confrère's
    /// invoice often arrives before or after hers; when it is set, that facture can state its own
    /// margin rather than only the dossier's.
    /// </summary>
    public Guid? InvoiceId { get; set; }
    public BillingInvoice? Invoice { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
