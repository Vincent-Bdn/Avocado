using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public sealed record FavouriteInput(bool IsFavourite);

/// <summary>
/// Pinning a dossier, on its own endpoint.
/// <para>
/// It could go through <see cref="UpdateMatter"/>, but that takes the whole dossier and would make a
/// one-click star a read-modify-write of every field — including the two litigation fields it
/// validates. A toggle should not be able to fail because the n° RG is elsewhere.
/// </para>
/// </summary>
public static class SetFavourite
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        FavouriteInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        matter.IsFavourite = input.IsFavourite;
        matter.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
