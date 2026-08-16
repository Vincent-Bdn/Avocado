using Avocado.Server.Data;
using Avocado.Server.Features.Vaults;
using Avocado.Server.Features.Vaults.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Checkout;

/// <summary>
/// Keeps every open dossier folder and the vault in step, without anyone asking.
///
/// <para>Polling rather than a FileSystemWatcher, for the reason the document workspace already
/// learned: watchers miss events under load, report a single save as three, and behave differently on
/// each platform and on network paths. Hashing what is there is slower and it is the truth.</para>
///
/// <para>Deletions are never applied on this path. A file that disappears mid-afternoon is far more
/// likely to be Word replacing it than a decision, and a background service must not remove a pièce
/// from a matter on that evidence. They wait for « J'ai terminé », where she sees them and confirms.</para>
/// </summary>
public sealed class CheckoutSyncService(
    VaultSession session,
    VaultDbContextFactory contexts,
    MatterCheckoutService checkouts,
    ILogger<CheckoutSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The startup pass, before anything else touches these folders. It never deletes: a folder a
        // crash left behind may hold an afternoon nobody saved.
        await ResumeAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Beat);

        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Checkout sync pass failed.");
            }
        }

        // On the way out, and outside the stopping token, which is already cancelled by now. Writes
        // back what changed and removes every folder it can; anything still held stays and is picked
        // up by the resumption check at the next launch.
        if (session.State == VaultState.Unlocked)
        {
            try
            {
                await checkouts.CloseAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not close open dossier folders on shutdown.");
            }
        }
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (session.State != VaultState.Unlocked)
        {
            return;
        }

        try
        {
            foreach (var (checkout, resumption) in await checkouts.ResumeAsync(cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Dossier folder {Folder} resumed: {Verdict}.", checkout.FolderPath, resumption.Verdict);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not check dossier folders at startup.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        if (session.State != VaultState.Unlocked)
        {
            return;
        }

        await using var database = contexts.Create(session.Get(Guid.Empty).Id);

        var open = await database.MatterCheckouts
            .Select(checkout => checkout.MatterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var matterId in open)
        {
            var changes = await checkouts.SyncAsync(matterId, applyDeletions: false, cancellationToken).ConfigureAwait(false);
            var notable = CheckoutReconciler.Notable(changes);

            if (notable.Count > 0)
            {
                logger.LogInformation(
                    "Dossier {Matter}: {Count} change(s) written back.", matterId, notable.Count);
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
