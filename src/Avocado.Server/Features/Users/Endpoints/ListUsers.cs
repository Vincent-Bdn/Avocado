using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Users.Endpoints;

public static class ListUsers
{
    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        CurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        // Resolved first so a fresh vault answers with its owner rather than an empty list.
        var current = await currentUser.GetAsync(cancellationToken);

        var users = await database.Users
            .AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(new { current = current.Id, users });
    }
}
