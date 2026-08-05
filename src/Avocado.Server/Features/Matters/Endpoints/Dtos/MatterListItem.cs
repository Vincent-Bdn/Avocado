using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Matters.Endpoints.Dtos;

/// <summary>
/// One row of the liste des dossiers. Eight columns, and nothing the table does not draw.
/// </summary>
/// <param name="ClientName">
/// A dossier can have several clients; the list shows one, deliberately. The first by creation order.
/// </param>
/// <param name="NextDeadlineDate">
/// Null renders as a muted dash. Always null on a closed matter — closing hides future échéances
/// rather than deleting them, so reopening brings them back.
/// </param>
/// <param name="LastActivityAt">Timestamp of the most recent journal entry. The client formats the
/// relative wording ("il y a 2 h", "hier").</param>
public sealed record MatterListItem(
    Guid Id,
    string Reference,
    string Name,
    string? ClientName,
    string? CourtCaseNumber,
    bool IsOpen,
    DateOnly? NextDeadlineDate,
    TimeOnly? NextDeadlineTime,
    DeadlineUrgency? NextDeadlineUrgency,
    DateTimeOffset? LastActivityAt);

/// <param name="Total">Total matching the filters, for « 1–40 sur 418 ».</param>
public sealed record MatterListPage(IReadOnlyList<MatterListItem> Items, int Total);
