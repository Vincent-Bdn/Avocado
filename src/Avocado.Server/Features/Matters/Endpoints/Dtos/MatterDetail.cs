using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Billings.ValueObjects;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Features.Matters.Endpoints.Dtos;

/// <param name="Role">Free text, and often long. The UI truncates and shows it in full on hover.</param>
public sealed record MatterPartyItem(
    Guid Id,
    Guid ContactId,
    ContactType ContactType,
    string DisplayName,
    bool IsClient,
    string? Role);

public sealed record MatterDeadlineItem(
    Guid Id,
    DateOnly Date,
    TimeOnly? Time,
    string Label,
    DeadlineUrgency Urgency);

/// <param name="RelativePath">From the dossier's folder, which is where the file actually is.</param>
/// <param name="ExhibitNumber">Set when Avocado wrote this file as a pièce.</param>
public sealed record MatterDocumentItem(
    string Name,
    string RelativePath,
    int? ExhibitNumber,
    DateTimeOffset ModifiedAt);

/// <summary>Drives the tab bar's counter pills.</summary>
public sealed record MatterCounts(int Activities, int Documents, int OpenDeadlines, int TimeEntries);

/// <param name="LastActivity">The one journal line the ⌘K preview shows under « Dernière entrée ».</param>
public sealed record MatterLastActivity(ActivityType Type, DateTimeOffset OccurredAt, string? Summary);

/// <summary>
/// Everything the fiche dossier header, tab bar and context panel need, plus exactly what the ⌘K
/// preview pane shows for a dossier, Statut, N° RG, Prochaine échéance, Reste à facturer and the last
/// entry, so the palette needs no endpoint of its own.
/// </summary>
public sealed record MatterDetail(
    Guid Id,
    string Reference,
    string Name,
    string? Description,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    long HourlyRateCents,
    string? CourtCaseNumber,
    string? Classification,
    string? Court,
    /// <summary>Where her documents for this dossier live, on her own disk. Null until she says.</summary>
    string? DocumentsFolder,
    bool IsOpen,
    bool IsFavourite,
    IReadOnlyList<MatterPartyItem> Parties,
    IReadOnlyList<MatterDeadlineItem> Deadlines,
    /// <summary>The five last modified, for the aperçu. The tab holds the other seven hundred.</summary>
    IReadOnlyList<MatterDocumentItem> Documents,
    MatterCounts Counts,
    BillingSummary Billing,
    MatterLastActivity? LastActivity);
