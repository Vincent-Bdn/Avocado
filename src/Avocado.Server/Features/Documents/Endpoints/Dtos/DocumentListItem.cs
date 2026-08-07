namespace Avocado.Server.Features.Documents.Endpoints.Dtos;

/// <param name="ExhibitLabel">
/// Line 1 on a pièce row. When null the row shows the file name in mono instead, that is the tell
/// that no libellé has been written yet.
/// </param>
/// <param name="OriginActivityId">
/// « Origine »: the journal entry the file arrived with. Null for a direct upload.
/// </param>
public sealed record DocumentListItem(
    Guid Id,
    int? ExhibitNumber,
    string? ExhibitLabel,
    string FileName,
    string? Folder,
    string? Type,
    long SizeBytes,
    string? MimeType,
    DateOnly? DocumentDate,
    DateTimeOffset AddedAt,
    Guid? OriginActivityId);

/// <param name="Folder">
/// Free text, « / » for nesting, normalised server-side. There is no folder table: a folder exists
/// exactly as long as a document names it.
/// </param>
public sealed record DocumentInput(
    string FileName,
    string? Folder,
    string? Type,
    DateOnly? DocumentDate)
{
    public string? Validate() =>
        string.IsNullOrWhiteSpace(FileName) ? "Le nom du fichier est obligatoire." : null;
}

/// <param name="FreeExhibitNumbers">
/// Gaps in the numbering, e.g. n° 10 after a pièce was withdrawn. Surfaced, never silently closed:
/// the numbers are cited in conclusions already filed, so renumbering has to be a deliberate act.
/// </param>
/// <param name="NextExhibitNumber">Pre-fills the promotion form.</param>
/// <param name="Folders">
/// Every folder currently in use on this dossier, so the filing field can offer what already exists
/// rather than inviting a fourth spelling of « Correspondance ».
/// </param>
public sealed record DocumentListPage(
    IReadOnlyList<DocumentListItem> Items,
    int Total,
    int ExhibitCount,
    long TotalSizeBytes,
    IReadOnlyList<int> FreeExhibitNumbers,
    int NextExhibitNumber,
    IReadOnlyList<string> Folders);
