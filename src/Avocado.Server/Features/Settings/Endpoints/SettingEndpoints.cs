using Avocado.Server.Data;
using Avocado.Server.Features.Settings.Endpoints.Dtos;
using Avocado.Server.Features.Settings.Infrastructure;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Settings.Endpoints;

public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/settings", GetSettings.HandleAsync).WithTags("Settings");
        routes.MapPut("/api/settings", UpdateSettings.HandleAsync).WithTags("Settings");

        // Likewise: a property of the machine, read once and cached, and deliberately not folded into
        // /api/settings, which every screen loads and which has no business shelling out to fdesetup.
        routes.MapGet("/api/system/disk-encryption", async (
            Avocado.Server.Features.Settings.Infrastructure.DiskEncryption disk,
            CancellationToken cancellationToken) =>
        {
            var status = await disk.ReadAsync(cancellationToken);

            return Results.Ok(new
            {
                status.State,
                status.Mechanism,
                pane = Avocado.Server.Features.Settings.Infrastructure.DiskEncryption.SettingsPane,
            });
        }).WithTags("Settings");

        return routes;
    }
}

public static class GetSettings
{
    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        return Results.Ok(new PracticeInfo(
            ReadLong(stored, PracticeSettingKeys.HourlyRateCents, PracticeSettingKeys.DefaultHourlyRateCents),
            PracticeAddresses.Parse(stored.GetValueOrDefault(PracticeSettingKeys.EmailAddresses)),
            vaultStore.Get(tenant.VaultId).Paths.Root,
            // Read back through the same parser the dashboard uses, so a value the figures ignore is
            // never a value the form shows as accepted.
            Dashboards.ValueObjects.Exercices.StartFrom(stored) is var (month, day)
                ? $"{month:00}-{day:00}"
                : "01-01"));
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

        // Null is « the form did not send them », an empty list is « she cleared them ». The rate form
        // sends only the rate, and conflating the two would wipe her addresses every time she changed
        // it.
        var addresses = input.EmailAddresses is null
            ? null
            : PracticeAddresses.Clean(input.EmailAddresses);

        if (addresses is { Rejected.Count: > 0 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["settings"] =
                [
                    "Ceci ne ressemble pas à une adresse : "
                    + string.Join(", ", addresses.Rejected),
                ],
            });
        }

        await UpsertAsync(
            database,
            PracticeSettingKeys.HourlyRateCents,
            input.HourlyRateCents.ToString(),
            cancellationToken);

        // « MM-DD », or nothing. Refused rather than silently ignored: an exercice starting on a
        // date the figures do not use would put a year's turnover in the wrong column.
        if (input.ExerciceStart is { } exercice)
        {
            if (Dashboards.ValueObjects.Exercices.StartFrom(
                    new Dictionary<string, string> { [PracticeSettingKeys.ExerciceStart] = exercice })
                is var parsed && $"{parsed.Month:00}-{parsed.Day:00}" != exercice)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["exerciceStart"] = ["Indiquez un début d'exercice entre le 1er et le 28 du mois."],
                });
            }

            await UpsertAsync(database, PracticeSettingKeys.ExerciceStart, exercice, cancellationToken);
        }

        if (addresses is not null)
        {
            await UpsertAsync(
                database,
                PracticeSettingKeys.EmailAddresses,
                string.Join('\n', addresses.Accepted),
                cancellationToken);
        }

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
