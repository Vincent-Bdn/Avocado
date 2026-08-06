using Avocado.Server.Data;
using Avocado.Server.Features.Settings.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Settings.Endpoints;

public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/settings", GetSettings.HandleAsync).WithTags("Settings");
        routes.MapPut("/api/settings", UpdateSettings.HandleAsync).WithTags("Settings");

        return routes;
    }
}

public static class GetSettings
{
    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        return Results.Ok(new PracticeSettings(
            ReadLong(stored, PracticeSettingKeys.HourlyRateCents, PracticeSettingKeys.DefaultHourlyRateCents)));
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> stored, string key, long fallback) =>
        stored.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : fallback;
}

public static class UpdateSettings
{
    public static async Task<IResult> HandleAsync(
        PracticeSettings input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.HourlyRateCents <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings"] = ["Le taux horaire doit être positif."],
            });
        }

        await UpsertAsync(
            database,
            PracticeSettingKeys.HourlyRateCents,
            input.HourlyRateCents.ToString(),
            cancellationToken);

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task UpsertAsync(
        AvocadoDbContext database,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await database.PracticeSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);

        if (existing is null)
        {
            database.PracticeSettings.Add(new PracticeSetting { Key = key, Value = value });
            return;
        }

        existing.Value = value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
