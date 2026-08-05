namespace Avocado.Server.Features.TimeEntries.Endpoints.Dtos;

/// <param name="FromActivityId">
/// Set when the entry came from a journal entry's `＋ temps passé` chip — the row appends
/// « · depuis le journal ».
/// </param>
/// <param name="AppliedRateCents">
/// The rate actually used: the entry's override, or the matter's frozen rate. Sent resolved so the
/// Taux column never has to reproduce the fallback rule.
/// </param>
/// <param name="IsRateOverridden">
/// Drives the ochre row treatment. « Je ne facture que la moitié », agreed in February, must still be
/// visible in June.
/// </param>
public sealed record TimeEntryListItem(
    Guid Id,
    DateOnly Date,
    TimeOnly? StartedAt,
    string Task,
    int DurationMinutes,
    bool IsBillable,
    long AppliedRateCents,
    bool IsRateOverridden,
    long AmountCents,
    Guid? FromActivityId);

/// <param name="MatterMinutes">« Total du dossier », alongside today and this week.</param>
public sealed record TimeEntryTotals(
    int TodayMinutes,
    int WeekMinutes,
    int MatterMinutes,
    int BillableMinutes,
    int NonBillableMinutes,
    long BillableAmountCents);

public sealed record TimeEntryListPage(IReadOnlyList<TimeEntryListItem> Items, TimeEntryTotals Totals);
