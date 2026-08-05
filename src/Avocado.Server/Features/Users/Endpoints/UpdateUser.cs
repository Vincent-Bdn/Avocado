using Avocado.Server.Data;
using Avocado.Server.Features.Users.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Users.Endpoints;

public static class UpdateUser
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        UserInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = [error] });
        }

        var user = await database.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (user is null)
        {
            return Results.NotFound();
        }

        user.DisplayName = input.DisplayName.Trim();
        user.Email = input.Email?.Trim();
        user.IsActive = input.IsActive;

        // Affects matters created from now on. Existing matters keep the rate frozen at their own
        // creation, which is the whole point of snapshotting it there.
        user.HourlyRateCents = input.HourlyRateCents;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
