using Avocado.Server.Features.Activities.Enums;

namespace Avocado.Server.Features.Activities.Endpoints.Dtos;

/// <param name="Name">Filename as stored. The client renders `nom.pdf · 1,4 Mo`.</param>
/// <param name="ExhibitNumber">Present when the document was promoted to a pièce.</param>
public sealed record ActivityAttachment(Guid Id, string Name, long SizeBytes, int? ExhibitNumber);

/// <summary>
/// One row of the journal.
/// </summary>
/// <param name="ContactName">
/// Just the name. Their role on this matter is deliberately not resolved here: it would mean a join
/// through MatterParty on every row, and the name alone answers "who was this with".
/// </param>
/// <param name="DurationMinutes">The ochre duration chip, when time was logged with the entry.</param>
public sealed record ActivityListItem(
    Guid Id,
    ActivityType Type,
    DateTimeOffset OccurredAt,
    Guid? ContactId,
    string? ContactName,
    string? Subject,
    string? Body,
    string? TrackingNumber,
    int? DurationMinutes,
    IReadOnlyList<ActivityAttachment> Attachments);

public sealed record ActivityListPage(IReadOnlyList<ActivityListItem> Items, int Total);
