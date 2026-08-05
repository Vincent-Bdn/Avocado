using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Activities.Endpoints;

/// <summary>
/// Deleting asks nothing — the design offers 8 seconds of undo in a toast instead of a confirmation
/// dialog. Attached documents and time entries survive: their foreign keys are <c>SetNull</c>, so an
/// undo that recreates the entry loses the link but never the billable time or the file.
/// </summary>
public static class DeleteActivity
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var activity = await database.Activities
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (activity is null)
        {
            return Results.NotFound();
        }

        database.Activities.Remove(activity);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
