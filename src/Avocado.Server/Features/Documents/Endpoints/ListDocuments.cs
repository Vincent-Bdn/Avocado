using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Endpoints.Dtos;
using Avocado.Server.Features.Documents.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

public static class ListDocuments
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        AvocadoDbContext database,
        DocumentSegment segment = DocumentSegment.All,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var all = database.Documents.AsNoTracking().Where(document => document.MatterId == matterId);

        var query = segment switch
        {
            DocumentSegment.Exhibits => all.Where(document => document.ExhibitNumber != null),
            DocumentSegment.Documents => all.Where(document => document.ExhibitNumber == null),
            _ => all,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(document =>
                EF.Functions.Like(document.FileName, pattern) ||
                EF.Functions.Like(document.ExhibitLabel ?? string.Empty, pattern) ||
                EF.Functions.Like(document.Type ?? string.Empty, pattern));
        }

        var items = await query
            // Pièces first and in numeric order, then everything else newest-first: the two section
            // headers the design draws are this ordering, not a client-side regroup.
            .OrderBy(document => document.ExhibitNumber == null)
            .ThenBy(document => document.ExhibitNumber)
            .ThenByDescending(document => document.AddedAt)
            .Select(document => new DocumentListItem(
                document.Id,
                document.ExhibitNumber,
                document.ExhibitLabel,
                document.FileName,
                document.Type,
                document.SizeBytes,
                document.MimeType,
                document.DocumentDate,
                document.AddedAt,
                document.ActivityId))
            .ToListAsync(cancellationToken);

        var usedNumbers = await all
            .Where(document => document.ExhibitNumber != null)
            .Select(document => document.ExhibitNumber!.Value)
            .OrderBy(number => number)
            .ToListAsync(cancellationToken);

        return Results.Ok(new DocumentListPage(
            items,
            await all.CountAsync(cancellationToken),
            usedNumbers.Count,
            await all.SumAsync(document => (long?)document.SizeBytes, cancellationToken) ?? 0,
            FreeNumbers(usedNumbers),
            NextNumber(usedNumbers)));
    }

    /// <summary>Gaps below the highest number in use — « n° 10 libre ».</summary>
    internal static IReadOnlyList<int> FreeNumbers(IReadOnlyList<int> used)
    {
        if (used.Count == 0)
        {
            return [];
        }

        var taken = used.ToHashSet();
        return [.. Enumerable.Range(1, used[^1]).Where(number => !taken.Contains(number))];
    }

    internal static int NextNumber(IReadOnlyList<int> used) => used.Count == 0 ? 1 : used[^1] + 1;
}
