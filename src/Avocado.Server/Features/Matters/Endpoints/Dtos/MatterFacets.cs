namespace Avocado.Server.Features.Matters.Endpoints.Dtos;

/// <param name="MatterCount">Shown beside the client in the filter panel's most-used list.</param>
public sealed record MatterClientFacet(Guid ContactId, string DisplayName, int MatterCount);

/// <summary>
/// Counts for the filter panel and the header sub-line. Computed over open matters, since a closed one
/// shows no deadline anywhere.
/// </summary>
public sealed record MatterFacets(
    int Open,
    int Closed,
    int Overdue,
    int WithinSevenDays,
    int WithinThirtyDays,
    int WithoutDeadline,
    IReadOnlyList<MatterClientFacet> TopClients,
    int OtherClientCount);
