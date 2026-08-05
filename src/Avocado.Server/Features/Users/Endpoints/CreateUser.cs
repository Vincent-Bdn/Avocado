using Avocado.Server.Data;
using Avocado.Server.Features.Users.Endpoints.Dtos;

namespace Avocado.Server.Features.Users.Endpoints;

public static class CreateUser
{
    public static async Task<IResult> HandleAsync(
        UserInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = [error] });
        }

        var user = new User
        {
            DisplayName = input.DisplayName.Trim(),
            Email = input.Email?.Trim(),
            HourlyRateCents = input.HourlyRateCents,
            IsActive = input.IsActive,
        };

        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/users/{user.Id}", new { user.Id });
    }
}
