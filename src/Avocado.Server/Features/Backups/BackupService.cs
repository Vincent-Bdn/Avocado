using Avocado.Server.Data;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Vaults;
using Avocado.Server.Features.Vaults.Enums;
using Avocado.Vault;
using Avocado.Vault.Backups;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups;

/// <summary>
/// Takes snapshots and gets copies off this machine, without anyone having to remember to.
///
/// <para><b>Why this is not a cron job, a scheduled task or a launchd plist.</b> The vault only
/// changes while Avocado is running. A scheduler firing at three in the morning against a closed
/// application would copy a vault byte-for-byte identical to the one it copied yesterday. So an
/// in-process scheduler cannot miss a change: the changes it would miss cannot happen. What it buys
/// on top of that is not having to register anything with the operating system, three times, with
/// three sets of permissions and three silent failure modes, and having the state visible in the
/// window rather than in a system console nobody opens.</para>
///
/// <para>It wakes on four occasions, which between them cover the ways work gets lost: when the vault
/// is opened and the last snapshot is stale, periodically while there is unsaved-to-backup work, when
/// a destination appears, and on the way out.</para>
/// </summary>
public sealed class BackupService(
    VaultSession session,
    VaultDbContextFactory contexts,
    SinkFactory sinks,
    TimeProvider clock,
    ILogger<BackupService> logger) : BackgroundService
{
    /// <summary>How often to look. Cheap: a file stat, and a probe per destination.</summary>
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(30);

    /// <summary>How long a working day is allowed to go without a new snapshot.</summary>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastSeenDatabaseStamp;

    /// <summary>The window's « Sauvegarder maintenant ». Same code path as the timer, deliberately.</summary>
    public Task<IReadOnlyList<DestinationOutcome>> RunNowAsync(CancellationToken cancellationToken) =>
        RunAsync(force: true, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Beat, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(force: false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // A backup that throws must never take the application with it. The next beat retries.
                logger.LogError(exception, "Backup pass failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }

        // On the way out, and outside the stopping token, because that token is already cancelled by
        // the time shutdown runs. This is the pass that catches the afternoon's work when someone
        // closes the laptop at six.
        try
        {
            await RunAsync(force: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Final backup on shutdown failed.");
        }
    }

    private async Task<IReadOnlyList<DestinationOutcome>> RunAsync(bool force, CancellationToken cancellationToken)
    {
        if (session.State != VaultState.Unlocked || !session.TryGet(Guid.Empty, out var opened) || opened is null)
        {
            return [];
        }

        // One pass at a time. The timer and « Sauvegarder maintenant » can otherwise collide, and two
        // mirrors uploading the same blob to the same destination is at best wasted bandwidth.
        if (!await _gate.WaitAsync(force ? Timeout.InfiniteTimeSpan : TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        try
        {
            return await PassAsync(opened, force, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<DestinationOutcome>> PassAsync(
        OpenVault vault,
        bool force,
        CancellationToken cancellationToken)
    {
        await using var database = contexts.Create(vault.Id);

        var destinations = await database.Set<BackupDestination>()
            .Where(destination => destination.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshot = EnsureSnapshot(vault, force);
        var results = new List<DestinationOutcome>();

        foreach (var destination in destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await PushAsync(vault, database, destination, snapshot, cancellationToken).ConfigureAwait(false));
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Takes a snapshot if the database has moved since the last one, or if enough time has passed.
    ///
    /// <para>« Has it changed » is answered by the file's size and write time rather than by counting
    /// saves. It costs one stat, it survives a restart, and it cannot drift out of step with reality
    /// the way a counter in memory can.</para>
    /// </summary>
    private VaultSnapshot? EnsureSnapshot(OpenVault vault, bool force)
    {
        var store = vault.Snapshots;
        var newest = store.Newest();

        var file = new FileInfo(vault.Paths.DatabaseFile);
        var stamp = file.Exists ? file.LastWriteTimeUtc.Ticks ^ file.Length : 0;
        var changed = stamp != _lastSeenDatabaseStamp;

        var stale = newest is null || clock.GetUtcNow() - newest.Value.TakenAt >= SnapshotInterval;

        if (newest is not null && !force && !(changed && stale))
        {
            return newest;
        }

        try
        {
            var taken = vault.CreateBackup("auto");
            _lastSeenDatabaseStamp = stamp;

            store.Prune(SnapshotRetention.Default, clock.GetUtcNow());
            return taken;
        }
        catch (VaultException exception)
        {
            // Two snapshots inside the same second collide on the file name. Harmless: the one that
            // already exists is the same instant's data.
            logger.LogDebug(exception, "Snapshot skipped.");
            return newest;
        }
    }

    private async Task<DestinationOutcome> PushAsync(
        OpenVault vault,
        AvocadoDbContext database,
        BackupDestination destination,
        VaultSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (sinks.Create(destination) is not { } sink)
        {
            destination.LastError = sinks.ExplainMissing(destination);
            return new DestinationOutcome(destination.Id, destination.Label, false, destination.LastError);
        }

        if (snapshot is null)
        {
            return new DestinationOutcome(destination.Id, destination.Label, false, "Aucune sauvegarde à envoyer.");
        }

        try
        {
            var outcome = await new BackupMirror(vault, sink)
                .PushAsync(
                    snapshot.Value,
                    new SnapshotRetention(destination.KeepNewest, destination.KeepDailyForDays),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Skipped)
            {
                // Unplugged. Not an error, and saying so would train her to ignore the one that is.
                return new DestinationOutcome(destination.Id, destination.Label, false, null);
            }

            destination.LastBackupAt = outcome.CompletedAt;
            destination.LastSeenAt = outcome.CompletedAt;
            destination.LastError = null;

            logger.LogInformation(
                "Backed up to {Destination}: {Blobs} documents, {Bytes} bytes.",
                destination.Label, outcome.BlobsUploaded, outcome.BytesUploaded);

            return new DestinationOutcome(destination.Id, destination.Label, true, null);
        }
        catch (Exception exception) when (exception is VaultException or IOException or UnauthorizedAccessException)
        {
            destination.LastError = exception.Message;
            logger.LogWarning(exception, "Backup to {Destination} failed.", destination.Label);

            return new DestinationOutcome(destination.Id, destination.Label, false, exception.Message);
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}

public sealed record DestinationOutcome(Guid Id, string Label, bool Succeeded, string? Error);
