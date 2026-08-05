using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.TimeEntries.Endpoints;

public static class DeleteTimeEntry
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var entry = await database.TimeEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        database.TimeEntries.Remove(entry);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
