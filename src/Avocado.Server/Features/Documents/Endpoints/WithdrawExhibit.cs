using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <summary>
/// « Retirer le n° de pièce », the document returns to the rank of document, and nothing is deleted.
/// The number it held is left free rather than reused automatically: it may already be cited in
/// conclusions that have been filed, so the gap is surfaced (« n° 10 libre ») and closing it is a
/// deliberate renumbering.
/// </summary>
public static class WithdrawExhibit
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var document = await database.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        document.ExhibitNumber = null;
        document.ExhibitLabel = null;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
