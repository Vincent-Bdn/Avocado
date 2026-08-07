using Avocado.Server.Data;
using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Activities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

/// <summary>
/// Closing sets the closing date and writes a journal line. It deletes nothing.
/// <para>
/// The liste-des-dossiers handoff says closing "clears" the échéances. It must not: the fiche dossier
/// promises « rien n'a été supprimé », and reopening would then be unable to restore them, silent,
/// irreversible loss on an action that reads as reversible. The behaviour the screens actually show,
/// no deadline on a closed dossier, and *Clôturés + dépassée* returning nothing, comes from the read
/// queries excluding closed matters, not from deleting rows.
/// </para>
/// </summary>
public static class CloseMatter
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

        if (matter.ClosedOn is not null)
        {
            return Results.NoContent();
        }

        var now = clock.GetLocalNow();
        matter.ClosedOn = DateOnly.FromDateTime(now.DateTime);
        matter.UpdatedAt = DateTimeOffset.UtcNow;

        database.Activities.Add(new Activity
        {
            MatterId = matter.Id,
            OccurredAt = now,
            Type = ActivityType.Note,
            Subject = "Dossier clôturé",
        });

        await database.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
}
