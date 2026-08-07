using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Dashboards.ValueObjects;

/// <param name="MatterName">A deadline without its dossier is unusable, so both always travel together.</param>
public sealed record DashboardDeadline(
    Guid Id,
    Guid MatterId,
    string MatterReference,
    string MatterName,
    string? ClientName,
    string Label,
    DateOnly Date,
    TimeOnly? Time,
    DeadlineUrgency Urgency);

/// <summary>One line of the per-dossier breakdown under the unbilled total.</summary>
public sealed record DashboardUnbilledMatter(
    Guid MatterId,
    string MatterName,
    int BillableMinutes,
    long LeftToBillCents);

/// <param name="AgedOverSixtyDaysCents">
/// Billable time recorded more than 60 days ago on matters that still have something left to bill.
/// A triage signal — « dont X de plus de 60 jours » — not an accounting figure.
/// </param>
/// <param name="Matters">Ordered by amount. The client shows four and aggregates the rest.</param>
public sealed record DashboardUnbilled(
    long TotalCents,
    int TotalBillableMinutes,
    int MatterCount,
    long AgedOverSixtyDaysCents,
    IReadOnlyList<DashboardUnbilledMatter> Matters);

/// <param name="LastActivitySummary">
/// The last journal line, prefixed by its type. The dossier name alone does not tell her where she was.
/// </param>
public sealed record DashboardRecentMatter(
    Guid Id,
    string Reference,
    string Name,
    string? ClientName,
    ActivityType? LastActivityType,
    string? LastActivitySummary,
    DateTimeOffset? LastActivityAt);

/// <param name="NextDeadlineBeyondHorizon">
/// Feeds the closing line « Aucune autre échéance avant le 12 avril », which reads better than a
/// truncation counter. Null when there is genuinely nothing further out.
/// </param>
public sealed record DashboardSummary(
    DateOnly Today,
    int OpenMatterCount,
    int ContactCount,
    int WithinSevenDaysCount,
    IReadOnlyList<DashboardDeadline> Deadlines,
    DateOnly? NextDeadlineBeyondHorizon,
    DashboardUnbilled Unbilled,
    IReadOnlyList<DashboardRecentMatter> RecentMatters,
    /// <summary>« Ai-je facturé ce que j'ai travaillé ? », twelve months of it.</summary>
    DashboardHonoraires Honoraires);
