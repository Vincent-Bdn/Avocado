using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Billings;

/// <summary>
/// A facture that was issued <em>elsewhere</em>. Avocado never generates an invoice, she has an
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

    /// <summary>
    /// A facture whose work was never recorded here: issued before Avocado, or in another system, and
    /// brought over for the history.
    ///
    /// <para><b>It exists because « reste à facturer » subtracts hand-recorded factures.</b> That
    /// subtraction is right for one she types today, since the hours justifying it are still sitting
    /// unbilled and would otherwise be counted twice. It is meaningless for one imported from
    /// Gestisoft: those hours were never entered, so nothing is being counted twice and the
    /// subtraction simply removes money that was never there. COULEYRE arrived with seven factures
    /// totalling 8 974 €, and the first 45 minutes she recorded on it read « reste à facturer
    /// − 8 794 € ».</para>
    ///
    /// <para>Editable rather than fixed at import, because the same thing is true of a facture she
    /// types in for last year.</para>
    /// </summary>
    public bool IsHistorical { get; set; }

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

    /// <summary>
    /// « Application d'une remise commerciale de 30% pour fidélité client ». In basis points, so
    /// 3 000 is 30%.
    ///
    /// <para>Basis points rather than a percentage, because a percentage that is not a whole number
    /// wants a decimal, and <c>AvocadoDbContext</c> refuses decimals for the reason every money
    /// column here is a <c>long</c>. Basis points say 12,5% exactly and sort correctly.</para>
    ///
    /// <para><b>Presentation, not arithmetic.</b> <see cref="AmountExclVatCents"/> is already the
    /// amount actually billed, remise deducted: it is what the client owes, what the accountant sees
    /// and what the chiffre d'affaires counts. This records why it is lower than the hours behind it,
    /// so the détail can say so in the sentence she would have written by hand.</para>
    /// </summary>
    public int? DiscountBasisPoints { get; set; }

    /// <summary>« pour fidélité client ». Free text, because the reason is hers.</summary>
    public string? DiscountReason { get; set; }

    /// <summary>
    /// The facture itself, as a file in her dossier's folder: « Factures/FACTURE_92_CLIENT.pdf ».
    ///
    /// <para>Relative, like everything else Avocado records about her documents, so it survives the
    /// folder moving or the practice being restored onto another machine. Null while a facture has
    /// only ever been typed in, which is most of them until she starts dropping the PDFs in.</para>
    ///
    /// <para><b>A path, not a blob.</b> Her factures belong with the rest of the dossier, where she
    /// can find them in Explorer without Avocado. Copying them into the coffre would put a second
    /// copy somewhere she cannot open, which is the arrangement ten lawyers rejected.</para>
    /// </summary>
    public string? DocumentPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Positive = boni (billed above the recorded time), negative = mali.</summary>
    public long VarianceCents => AmountExclVatCents - BilledTimeCents;
}
