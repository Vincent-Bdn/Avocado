using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Searches.ValueObjects;

/// <param name="Label">The main text. « nom · client » for a dossier, the name for a tiers.</param>
/// <param name="Meta">
/// The right-hand text: the mono référence, « rôle · N dossiers », « pièce n° 9 » or « document ».
/// </param>
/// <param name="ContactType">Set on a tiers, so the client picks round or rounded-square for the avatar.</param>
public sealed record SearchResultItem(
    Guid Id,
    string Label,
    string? Meta,
    ContactType? ContactType = null);

/// <param name="Total">Total matches, so the client can offer « voir les N autres ».</param>
public sealed record SearchResultGroup(string Key, IReadOnlyList<SearchResultItem> Items, int Total);

public sealed record SearchResults(IReadOnlyList<SearchResultGroup> Groups, int Total);

/// <param name="LastActivityAt">Right-hand relative time, formatted by the client.</param>
public sealed record RecentMatterItem(
    Guid Id,
    string Reference,
    string Label,
    DateTimeOffset? LastActivityAt);

public sealed record NearestDeadlineItem(
    Guid Id,
    Guid MatterId,
    string Label,
    string MatterName,
    DateOnly Date,
    TimeOnly? Time,
    DeadlineUrgency Urgency);

/// <summary>
/// The empty-query state: where was I, and what falls due. No preview pane, so the palette stays short.
/// </summary>
public sealed record SearchStartingPoints(
    IReadOnlyList<RecentMatterItem> RecentMatters,
    IReadOnlyList<NearestDeadlineItem> NearestDeadlines);
