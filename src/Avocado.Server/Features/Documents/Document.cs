using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Documents;

/// <summary>
/// Any file attached to a matter. The bytes live in the encrypted blob store; this row holds only the
/// reference and the metadata.
/// <para>
/// A document <em>becomes</em> a <b>pièce</b> when it is given a number and a libellé. That is a 1:1
/// relationship, hence two nullable columns rather than a separate table. In French procedure pièces
/// are the evidence communicated to the other side, numbered and cited in conclusions (« la pièce
/// n° 7 »); conclusions and correspondence with one's own client are never pièces.
/// </para>
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    /// <summary>The journal entry that brought the file in, when there was one.</summary>
    public Guid? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Hex SHA-256 of the plaintext — the <c>BlobReference</c> the vault stores under.</summary>
    public string BlobSha256 { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? MimeType { get; set; }

    /// <summary>
    /// The « Type » column: Contrat, Extrait, Comptable, Attestation, Acte, Rapport, Écriture,
    /// Courrier, Procédure, Note, Photo, Statuts… Free text, like <c>MatterParty.Role</c> and for the
    /// same reason — the UI offers the common ones, and a new kind never needs a release.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// The folder this file is filed under, as a plain string: « Procédure », « Correspondance »,
    /// « Pièces adverses ». There is no folder table and no tree — a folder exists exactly as long as
    /// a document names it, which is what stops an empty hierarchy accumulating around three files.
    /// Nested folders are written with « / » and grouped by the client.
    /// </summary>
    public string? Folder { get; set; }

    /// <summary>The date on the document itself, which is rarely the date it was filed.</summary>
    public DateOnly? DocumentDate { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when this document is promoted to a numbered exhibit. Unique within the matter.</summary>
    public int? ExhibitNumber { get; set; }

    /// <summary>
    /// The description written for the judge — « Contrat de travail de M. Dupont du 12 mars 2019 »,
    /// never the file name.
    /// </summary>
    public string? ExhibitLabel { get; set; }

    public bool IsExhibit => ExhibitNumber is not null;
}
