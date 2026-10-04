using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

/// <summary>
/// The natures d'affaire this practice actually uses, most used first.
///
/// <para>The field has always been free text with two suggestions compiled in, « Contentieux » and
/// « Conseil », which is two more than nothing and fewer than any practice needs. A lawyer asked to
/// be able to choose rather than retype; a closed list would mean a release every time someone takes
/// on a nature nobody thought of, so the list is simply what she has already written.</para>
///
/// <para>Closed dossiers count. « Arbitrage » typed once last year is exactly the spelling she wants
/// offered when she types it again, and leaving it out would hand her two spellings of one thing.</para>
/// </summary>
public static class ListClassifications
{
    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var used = await database.Matters
            .AsNoTracking()
            .Where(matter => matter.Classification != null && matter.Classification != "")
            .GroupBy(matter => matter.Classification!)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(used.Select(entry => entry.Name));
    }
}
