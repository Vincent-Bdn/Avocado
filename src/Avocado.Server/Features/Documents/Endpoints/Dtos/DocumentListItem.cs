namespace Avocado.Server.Features.Documents.Endpoints.Dtos;

/// <param name="ExhibitLabel">
/// Line 1 on a pièce row. When null the row shows the file name in mono instead — that is the tell
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
    string? Type,
    long SizeBytes,
    string? MimeType,
    DateOnly? DocumentDate,
    DateTimeOffset AddedAt,
    Guid? OriginActivityId);

/// <param name="FreeExhibitNumbers">
/// Gaps in the numbering, e.g. n° 10 after a pièce was withdrawn. Surfaced, never silently closed:
/// the numbers are cited in conclusions already filed, so renumbering has to be a deliberate act.
/// </param>
/// <param name="NextExhibitNumber">Pre-fills the promotion form.</param>
public sealed record DocumentListPage(
    IReadOnlyList<DocumentListItem> Items,
    int Total,
    int ExhibitCount,
    long TotalSizeBytes,
    IReadOnlyList<int> FreeExhibitNumbers,
    int NextExhibitNumber);
