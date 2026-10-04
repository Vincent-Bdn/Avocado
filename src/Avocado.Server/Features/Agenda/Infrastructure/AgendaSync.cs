using Avocado.Server.Data;
using Avocado.Server.Features.Agenda.ValueObjects;
using Avocado.Server.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Agenda.Infrastructure;

/// <param name="Sent">How many events were created, changed or removed.</param>
public sealed record AgendaSyncResult(bool Ran, int Sent, string? Error);

/// <summary>
/// One pass: read the échéances, read what has been pushed, send the difference.
///
/// <para><b>Reconciling rather than reacting.</b> Nothing hooks the endpoints that create and delete
/// échéances. A pass diffs the two sides, which means a push that failed, a laptop that was closed,
/// an event somebody deleted in Google and a restore from a backup all heal on the next run, where a
/// chain of event handlers would have to get every one of those right separately.</para>
///
/// <para><b>Closed dossiers keep their échéances.</b> Closing hides them in Avocado; removing them
/// from her agenda would erase last year's audiences from her calendar, which is a record of where
/// she was. New ones stop appearing, and that is the whole difference.</para>
/// </summary>
public sealed class AgendaSync(
    AgendaConnection connection,
    IGoogleCalendar calendar,
    TimeProvider clock,
    ILogger<AgendaSync> log)
{
    public async Task<AgendaSyncResult> RunAsync(
        AvocadoDbContext database,
        bool force,
        CancellationToken cancellationToken)
    {
        var state = await connection.ReadAsync(database, cancellationToken);

        if (!state.IsConnected || !state.IsEnabled)
        {
            return new AgendaSyncResult(Ran: false, Sent: 0, Error: null);
        }

        // Five failures, then it waits to be asked again. A loop that retries a revoked token every
        // five minutes for a week burns her quota and tells nobody anything.
        if (!force && state.FailureCount >= AgendaSettingKeys.MaxFailures)
        {
            return new AgendaSyncResult(Ran: false, Sent: 0, Error: state.Error);
        }

        try
        {
            var sent = await PushAsync(database, cancellationToken);

            await connection.RecordSuccessAsync(database, clock.GetUtcNow(), cancellationToken);

            return new AgendaSyncResult(Ran: true, Sent: sent, Error: null);
        }
        catch (Exception failure) when (failure is GoogleAgendaException or HttpRequestException
                                            or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var message = failure is GoogleAgendaException
                ? failure.Message
                : "L’agenda Google n’est pas joignable.";

            log.LogWarning(failure, "Agenda push failed: {Message}", message);

            await connection.RecordFailureAsync(database, message, cancellationToken);

            return new AgendaSyncResult(Ran: true, Sent: 0, Error: message);
        }
    }

    private async Task<int> PushAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var access = await connection.AccessTokenAsync(database, cancellationToken);
        var calendarId = await connection.CalendarIdAsync(database, access, calendar, cancellationToken);

        var wanted = await WantedAsync(database, cancellationToken);
        var pushed = await database.AgendaEvents.ToListAsync(cancellationToken);

        var live = await database.Deadlines
            .AsNoTracking()
            .Select(deadline => deadline.Id)
            .ToListAsync(cancellationToken);

        var actions = AgendaPlan.For(wanted, pushed, live.ToHashSet());

        if (actions.Count == 0)
        {
            return 0;
        }

        var byDeadline = pushed.ToDictionary(evt => evt.DeadlineId);

        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (action.Kind)
            {
                case AgendaActionKind.Insert:
                    database.AgendaEvents.Add(new AgendaEvent
                    {
                        DeadlineId = action.DeadlineId,
                        GoogleEventId = await calendar.InsertAsync(
                            access, calendarId, action.Entry!, cancellationToken),
                        Fingerprint = action.Entry!.Fingerprint,
                        SyncedAt = clock.GetUtcNow(),
                    });
                    break;

                case AgendaActionKind.Update:
                    try
                    {
                        await calendar.UpdateAsync(
                            access, calendarId, action.EventId!, action.Entry!, cancellationToken);

                        var row = byDeadline[action.DeadlineId];
                        row.Fingerprint = action.Entry!.Fingerprint;
                        row.SyncedAt = clock.GetUtcNow();
                    }
                    catch (AgendaEventGoneException)
                    {
                        // She deleted it in her agenda. Put it back: the dossier is the record, and
                        // an échéance that silently stopped appearing is the failure this exists to
                        // prevent.
                        database.AgendaEvents.Remove(byDeadline[action.DeadlineId]);

                        database.AgendaEvents.Add(new AgendaEvent
                        {
                            DeadlineId = action.DeadlineId,
                            GoogleEventId = await calendar.InsertAsync(
                                access, calendarId, action.Entry!, cancellationToken),
                            Fingerprint = action.Entry!.Fingerprint,
                            SyncedAt = clock.GetUtcNow(),
                        });
                    }

                    break;

                case AgendaActionKind.Delete:
                    await calendar.DeleteAsync(access, calendarId, action.EventId!, cancellationToken);
                    database.AgendaEvents.Remove(byDeadline[action.DeadlineId]);
                    break;
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        return actions.Count;
    }

    /// <summary>What should be in her agenda: every échéance of a dossier that is not closed.</summary>
    private static async Task<List<AgendaEntry>> WantedAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken) =>
        await database.Deadlines
            .AsNoTracking()
            .Where(deadline => deadline.Matter!.ClosedOn == null)
            .Select(deadline => new AgendaEntry(
                deadline.Id,
                deadline.Label,
                deadline.Type,
                deadline.Matter!.Reference,
                deadline.Matter.Name,
                deadline.Date,
                deadline.Time,
                deadline.RemindDaysBefore,
                deadline.IsDone))
            .ToListAsync(cancellationToken);
}

/// <summary>
/// The pass, on a timer. Every quarter of an hour, which is far finer than an échéance needs and
/// coarse enough to be invisible; pressing « Synchroniser » in Réglages runs one immediately.
/// </summary>
public sealed class AgendaSyncService(IServiceProvider services, ILogger<AgendaSyncService> log)
    : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The vault is usually still locked when the host starts, and a pass against a locked vault
        // is an exception in a log nobody reads.
        using var timer = new PeriodicTimer(Period);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = services.CreateScope();

                await scope.ServiceProvider.GetRequiredService<AgendaSync>().RunAsync(
                    scope.ServiceProvider.GetRequiredService<AvocadoDbContext>(),
                    force: false,
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception failure)
            {
                // A locked vault, a database being migrated. The next tick tries again.
                log.LogDebug(failure, "Agenda pass skipped.");
            }
        }
    }
}
