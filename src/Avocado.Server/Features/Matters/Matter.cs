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
    /// never reach a court, and the header omits the segment entirely rather than showing a dash.
    /// Indexed because when the greffe telephones they quote this, not a name.
    /// </summary>
    public string? CourtCaseNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MatterParty> Parties { get; set; } = [];

    public bool IsOpen => ClosedOn is null;
}
