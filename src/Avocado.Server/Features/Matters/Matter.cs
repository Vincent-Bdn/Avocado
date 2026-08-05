using Avocado.Server.Features.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avocado.Server.Features.Matters;

/// <summary>A dossier. The central object; everything else hangs off it.</summary>
public class Matter
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Auto-generated as <c>YYYY-NNNN</c>, overridable so existing references carry over.</summary>
    public string Reference { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly OpenedOn { get; set; }

    /// <summary>Null means *en cours*. This is the status; there is no separate field.</summary>
    public DateOnly? ClosedOn { get; set; }

    /// <summary>
    /// Snapshotted from the practice default when the matter is created, and never resolved
    /// dynamically: raising the default rate must not silently reprice two years of history.
    /// </summary>
    public long HourlyRateCents { get; set; }

    /// <summary>
    /// N° RG — the court's docket number. Nullable because advisory work, drafting and transactions
    /// never reach a court. Indexed because when the greffe telephones they quote this, not a name.
    /// </summary>
    public string? CourtCaseNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MatterParty> Parties { get; set; } = [];

    public bool IsOpen => ClosedOn is null;
}

/// <summary>
/// Links a contact to a matter. <see cref="Role"/> is free text so a new kind of party never needs a
/// release; <see cref="IsClient"/> stays structural because "who is this matter for" and "who do I
/// bill" have to be answerable.
/// </summary>
public class MatterParty
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public Guid ContactId { get; set; }
    public Contact? Contact { get; set; }

    public bool IsClient { get; set; }

    /// <summary>« Partie adverse », « Avocat de la partie adverse », « Expert judiciaire »…</summary>
    public string? Role { get; set; }
}

internal sealed class MatterConfiguration : IEntityTypeConfiguration<Matter>
{
    public void Configure(EntityTypeBuilder<Matter> builder)
    {
        builder.ToTable("matters");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Reference).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(300).IsRequired();
        builder.Property(m => m.CourtCaseNumber).HasMaxLength(64);

        builder.Ignore(m => m.IsOpen);

        builder.HasIndex(m => m.Reference).IsUnique();
        builder.HasIndex(m => m.CourtCaseNumber);
        builder.HasIndex(m => m.ClosedOn);
    }
}

internal sealed class MatterPartyConfiguration : IEntityTypeConfiguration<MatterParty>
{
    public void Configure(EntityTypeBuilder<MatterParty> builder)
    {
        builder.ToTable("matter_parties");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Role).HasMaxLength(120);

        builder.HasOne(p => p.Matter)
            .WithMany(m => m.Parties)
            .HasForeignKey(p => p.MatterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a contact who is still party to a matter would leave the matter unattributable.
        builder.HasOne(p => p.Contact)
            .WithMany()
            .HasForeignKey(p => p.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.MatterId, p.ContactId }).IsUnique();
        builder.HasIndex(p => p.ContactId);
    }
}
