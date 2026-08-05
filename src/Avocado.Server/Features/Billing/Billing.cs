using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Billing;

/// <summary>
/// A facture that was issued <em>elsewhere</em>. Avocado never generates an invoice — she has an
/// invoicing platform for that. This records what was billed so « reste à facturer » can be right.
/// </summary>
public class Invoice
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

/// <summary>
/// Money that moved <em>without</em> an invoice: a provision received, a débours advanced, a
/// correction. Signed — <b>positive = received from the client, negative = advanced on the matter</b>.
/// <para>
/// The boundary rule, and the only place this model can silently produce a wrong number: an amount is
/// either an <see cref="Invoice"/> or a <see cref="LedgerEntry"/>, never both. Entering a provision
/// that was itself invoiced as both would double-count it.
/// </para>
/// <para>
/// The UI must never expose the sign as a raw field. Two buttons — <i>Encaissement</i> and
/// <i>Débours</i> — set it, because a débours typed as a positive number makes every balance wrong
/// while still looking plausible.
/// </para>
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public DateOnly Date { get; set; }

    public long AmountCents { get; set; }

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// « Détail à facturer » for one matter. Not an invoice — the figures to paste into whatever issues
/// the invoice.
/// </summary>
/// <param name="LeftToBillCents">
/// <c>billable time − ledger − already invoiced</c>. Can be negative, which means the client is in
/// credit, and that has to be shown rather than clamped to zero.
/// </param>
public sealed record BillingSummary(
    long BillableTimeCents,
    int BillableMinutes,
    long LedgerCents,
    long InvoicedCents,
    long LeftToBillCents);

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ExternalReference).HasMaxLength(120);

        builder.HasOne(i => i.Matter)
            .WithMany()
            .HasForeignKey(i => i.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.MatterId, i.Date });
        builder.HasIndex(i => i.IsPaid);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Label).HasMaxLength(300).IsRequired();

        builder.HasOne(l => l.Matter)
            .WithMany()
            .HasForeignKey(l => l.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.MatterId, l.Date });
    }
}
