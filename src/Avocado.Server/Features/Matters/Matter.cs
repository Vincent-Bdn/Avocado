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
    /// N° RG, the court's docket number. Nullable because advisory work, drafting and transactions
    /// never reach a court, and the header omits the segment entirely rather than showing a dash.
    /// Indexed because when the greffe telephones they quote this, not a name.
    /// </summary>
    public string? CourtCaseNumber { get; set; }

    /// <summary>
    /// « Conseil » or « Contentieux » in practice, but stored as free text: a practice that also does
    /// arbitrage or médiation should be able to say so without waiting for a release. Only the exact
    /// word « Contentieux » unlocks the two litigation fields below, and that comparison is the one
    /// piece of vocabulary the application interprets.
    /// </summary>
    public string? Classification { get; set; }

    /// <summary>« TC Lyon », « CA Grenoble ». Meaningless outside a contentieux, hence nullable.</summary>
    public string? Court { get; set; }

    /// <summary>
    /// Pinned to the top of the list, above a divider. Deliberately not a folder or a colour: what a
    /// solo practice needs is « the four I am living in this month », and anything richer becomes a
    /// filing system nobody maintains.
    /// </summary>
    public bool IsFavourite { get; set; }

    /// <summary>
    /// Where her documents for this dossier actually live, absolute, on her own disk.
    ///
    /// <para><b>Avocado does not own these files.</b> Ten lawyers said the same thing: they already
    /// have a place for their documents, arranged the way they want it, and an application that took
    /// copies into an encrypted store they had to check files out of was work rather than help. So
    /// this is a path, the Documents tab lists what is in it, and Explorer is where it is edited.</para>
    ///
    /// <para>Null while a dossier has not been pointed at one, which is every dossier until she says
    /// otherwise and every dossier created before this existed.</para>
    /// </summary>
    public string? DocumentsFolder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MatterParty> Parties { get; set; } = [];

    public bool IsOpen => ClosedOn is null;
}
