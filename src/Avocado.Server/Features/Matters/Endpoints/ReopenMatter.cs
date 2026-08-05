using Avocado.Server.Data;
using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Activities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

/// <summary>
/// Clears the closing date, and — as the Clôturé screen promises — « le journal note la réouverture ».
/// Every deadline hidden by the closure comes back, because none of them were deleted.
/// </summary>
public static class ReopenMatter
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        if (matter.ClosedOn is null)
        {
            return Results.NoContent();
        }

        var closedOn = matter.ClosedOn.Value;
        var now = clock.GetLocalNow();

        matter.ClosedOn = null;
        matter.UpdatedAt = DateTimeOffset.UtcNow;

        database.Activities.Add(new Activity
        {
            MatterId = matter.Id,
            OccurredAt = now,
            Type = ActivityType.Note,
            Subject = "Dossier rouvert",
            Body = $"Clôturé le {closedOn:dd/MM/yyyy}, rouvert le {now:dd/MM/yyyy}.",
        });

        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
