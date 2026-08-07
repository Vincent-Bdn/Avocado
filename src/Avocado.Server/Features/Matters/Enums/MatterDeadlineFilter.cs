namespace Avocado.Server.Features.Matters.Enums;

/// <summary>The « Échéance à venir » checkboxes. Cumulative windows, not disjoint buckets.</summary>
public enum MatterDeadlineFilter
{
    Overdue,
    WithinSevenDays,
    WithinThirtyDays,

    /// <summary>Sans échéance, no open deadline at all.</summary>
    None,
}
