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
    /// <para><b>Presentation only, now.</b> It used to decide arithmetic, because « reste à facturer »
    /// subtracted hand-recorded factures and subtracting an imported one removed money that had never
    /// been counted: COULEYRE arrived with seven factures totalling 8 974 € and her first 45 minutes
    /// of work read « reste à facturer − 8 794 € ». Factures now consume the lines they bill, so one
    /// that consumed nothing subtracts nothing and the special case has no work left to do. What it
    /// still says is « this is turnover from before Avocado », which the chart draws apart.</para>
    /// </summary>
    public bool IsHistorical { get; set; }

    /// <summary>
    /// Time she has decided she will never bill, written off on purpose.
    ///
    /// <para>« Sur ce dossier j'ai 1 500 en temps passé, j'ai facturé 1 000 et ne pourrai pas
    /// facturer plus. » Without this the hours sit in « reste à facturer » for ever and the dossier
    /// never settles. An abandon consumes its lines exactly as a facture does, carries no amount, and
    /// is therefore a mali of everything it consumed. It is not turnover and never appears in
    /// « facturé ».</para>
    ///
    /// <para>The same record as a facture rather than a table of its own: it does the one thing that
    /// matters here, which is taking lines out of the unbilled total, and deleting it gives them back
    /// through the same foreign key.</para>
    /// </summary>
    public bool IsAbandon { get; set; }

    /// <summary>
    /// A facture recorded before factures consumed lines, which must go on subtracting from
    /// « reste à facturer » although it consumed nothing.
    ///
    /// <para><b>Set by the migration and by nothing else, ever.</b> Ten practices were already
    /// working when the model changed. Their hand-recorded factures have no lines, because there was
    /// no way to attach any; under the new rule they would subtract nothing and every dossier
    /// carrying one would jump up by that amount overnight. Which hours each of them actually paid
    /// for is not knowable after the fact.</para>
    ///
    /// <para>So they keep their old behaviour and nothing new is ever written with this set. It is
    /// invisible in the window, it cannot be turned on, and it empties itself as those factures age
    /// out of the dossiers that are still open.</para>
    /// </summary>
    public bool DeductsWithoutLines { get; set; }

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
