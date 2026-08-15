using Avocado.Server.Data;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Backups.ValueObjects;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Endpoints;

/// <summary>
/// Everything the Sauvegarde screen and the header indicator need, in one call, so the two can never
/// disagree about whether the practice is safe.
/// </summary>
public static class GetBackupStatus
{
    public static async Task<BackupStatus> HandleAsync(
        AvocadoDbContext database,
        IVaultStore vaults,
        SinkFactory sinks,
        CancellationToken cancellationToken)
    {
        var vault = vaults.Get(Guid.Empty);

        var destinations = await database.Set<BackupDestination>()
            .OrderBy(destination => destination.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var views = new List<BackupDestinationView>();

        foreach (var destination in destinations)
        {
            var sink = sinks.Create(destination);

            // Probed on every load rather than cached: this is what makes plugging a key in show up
            // on the screen without anyone pressing anything, and a folder probe is a stat call.
            var probe = sink is null
                ? null
                : await sink.ProbeAsync(cancellationToken).ConfigureAwait(false);

            // A volume is off-machine by construction: it only became one by being enumerated as
            // removable or network. A folder is judged on where it actually points, today.
            var verdict = destination.Kind == BackupDestinationKinds.Folder && destination.Path is { } configured
                ? DestinationReachInspector.Inspect(configured, vault.Paths.Root)
                : new ReachVerdict(DestinationReach.OffMachine, "Support amovible ou réseau.");

            views.Add(new BackupDestinationView(
                destination.Id,
                destination.Kind,
                destination.Label,
                destination.Path,
                destination.IsEnabled,
                probe?.Status.ToString() ?? "Unreachable",
                verdict.Reach.ToString(),
                verdict.Detail,
                probe?.Location,
                destination.LastBackupAt,
                destination.LastError ?? (sink is null ? sinks.ExplainMissing(destination) : probe?.Detail),
                destination.KeepNewest,
                destination.KeepDailyForDays));
        }

        var snapshots = vault.Snapshots.List();

        // The newest copy that is genuinely somewhere else. Only off-machine destinations count,
        // and that restriction is the whole point: a folder beside the vault takes a real copy and
        // reports real success, and counting it here is what let the screen say « vous ne perdriez
        // rien » to someone whose only copy was on the disk about to fail.
        //
        // Disabled destinations still count. Turning one off does not un-write what it already holds.
        var offMachine = views
            .Where(view => view.Reach == nameof(DestinationReach.OffMachine))
            .Select(view => view.Id)
            .ToHashSet();

        var exposedSince = destinations
            .Where(destination => destination.LastBackupAt is not null && offMachine.Contains(destination.Id))
            .Max(destination => destination.LastBackupAt);

        return new BackupStatus(
            exposedSince,
            snapshots.Count == 0 ? null : snapshots[0].TakenAt,
            snapshots.Count,
            destinations.Count > 0,
            offMachine.Count > 0,
            views.Any(view => view is { IsEnabled: true, Status: "Ready" }),
            await MeasureExposureAsync(database, exposedSince, cancellationToken).ConfigureAwait(false),
            views);
    }

    /// <summary>
    /// The work done since <paramref name="since"/>, which is the work that only exists on this
    /// machine. When nothing has ever been backed up the answer is everything, because it is.
    /// </summary>
    private static async Task<BackupExposure> MeasureExposureAsync(
        AvocadoDbContext database,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        var horizon = since ?? DateTimeOffset.MinValue;

        var activities = await database.Activities
            .CountAsync(activity => activity.CreatedAt > horizon, cancellationToken).ConfigureAwait(false);

        var documents = await database.Documents
            .CountAsync(document => document.AddedAt > horizon, cancellationToken).ConfigureAwait(false);

        // Counted and summed in one pass: « 11 h 20 » is what makes the sentence land, and a count of
        // rows does not carry it.
        var time = await database.TimeEntries
            .Where(entry => entry.CreatedAt > horizon)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Minutes = group.Sum(entry => entry.DurationMinutes) })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new BackupExposure(activities, documents, time?.Count ?? 0, time?.Minutes ?? 0);
    }
}
