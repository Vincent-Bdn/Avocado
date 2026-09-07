using Avocado.Server.Data;
using Avocado.Server.Features.Settings.Endpoints.Dtos;
using Avocado.Server.Features.Settings.Infrastructure;
using Avocado.Server.Features.Documents.Workspace;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Settings.Endpoints;

public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/settings", GetSettings.HandleAsync).WithTags("Settings");
        routes.MapPut("/api/settings", UpdateSettings.HandleAsync).WithTags("Settings");

        // Its own route: this belongs to the computer, not to the practice, and is stored on the
        // machine rather than in the vault.
        routes.MapPut("/api/settings/working-directory", SetWorkingDirectory.HandleAsync).WithTags("Settings");

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
        WorkingDirectory workingDirectory,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        return Results.Ok(new PracticeInfo(
            ReadLong(stored, PracticeSettingKeys.HourlyRateCents, PracticeSettingKeys.DefaultHourlyRateCents),
            PracticeAddresses.Parse(stored.GetValueOrDefault(PracticeSettingKeys.EmailAddresses)),
            vaultStore.Get(tenant.VaultId).Paths.Root,
            // The folder she chose, not the per-vault subfolder inside it: that subfolder is an
            // implementation detail and offering it as the thing to change would be misleading.
            workingDirectory.Root,
            workingDirectory.IsOverridden));
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
