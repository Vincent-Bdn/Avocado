using Avocado.Server.Data;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Settings;
using Avocado.Vault;
using Avocado.Vault.Backups;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Endpoints;

/// <param name="Hour">Local, 0 to 23.</param>
public sealed record CaptureScheduleInput(bool IsEnabled, int Hour);

/// <param name="Ongoing">Where the dossiers en cours go. May be the same as <paramref name="Closed"/>.</param>
public sealed record RestoreDocumentsInput(string Ongoing, string Closed);

/// <summary>
/// The two things she can do with the documents half of a sauvegarde: say when it runs, and, on a new
/// machine, ask for it all back.
/// </summary>
public static class ManageDocumentCapture
{
    /// <param name="MatterIds">The dossiers to forget the folder of.</param>
    public sealed record DetachInput(IReadOnlyList<Guid> MatterIds);

    /// <summary>
    /// Forgets where a dossier's documents were, on her say-so.
    ///
    /// <para>Never done by the copy itself. She keeps the current dossiers on the laptop and the rest
    /// on the NAS, so a folder that is not here is usually one she archived deliberately, and a disk
    /// left at home would otherwise detach every dossier on it in one pass and leave her re-pointing
    /// each one. Nothing is removed from any destination: the copy up there is the point.</para>
    /// </summary>
    public static async Task<IResult> DetachAsync(
        DetachInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.MatterIds.Count == 0)
        {
            return Results.NoContent();
        }

        var detached = await database.Matters
            .Where(matter => input.MatterIds.Contains(matter.Id) && matter.DocumentsFolder != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(matter => matter.DocumentsFolder, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { detached });
    }

    public static async Task<IResult> SetScheduleAsync(
        CaptureScheduleInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Hour is < 0 or > 23)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["hour"] = ["L'heure doit être comprise entre 0 et 23."],
            });
        }

        await UpsertAsync(database, PracticeSettingKeys.CaptureDocuments, input.IsEnabled ? "true" : "false", cancellationToken)
            .ConfigureAwait(false);

        await UpsertAsync(database, PracticeSettingKeys.CaptureHour, input.Hour.ToString(), cancellationToken)
            .ConfigureAwait(false);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    /// <summary>
    /// Writes every captured document back onto this machine, into two folders she picks.
    ///
    /// <para>Separate from restoring the vault, and after it, on purpose. Rebuilding the coffre is a
    /// few megabytes and puts the practice's records back in minutes; writing twelve gigabytes of
    /// documents is a different order of operation, it needs somewhere to put them, and it is the
    /// step she may want to run later, or onto a disk she has not plugged in yet.</para>
    /// </summary>
    public static async Task<IResult> RestoreAsync(
        RestoreDocumentsInput input,
        AvocadoDbContext database,
        IVaultStore vaults,
        SinkFactory sinks,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Ongoing) || string.IsNullOrWhiteSpace(input.Closed))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["folders"] = ["Indiquez où placer les dossiers en cours et les dossiers clôturés."],
            });
        }

        // The documents are on a destination, in clear, so one has to be reachable to get them
        // back. Whichever answers first: they hold the same thing, and she should not have to know
        // which of her two copies this one came from.
        var destinations = await database.Set<BackupDestination>()
            .Where(destination => destination.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IBackupSink? ready = null;

        foreach (var candidate in destinations.Select(sinks.Create).OfType<IBackupSink>())
        {
            if ((await candidate.ProbeAsync(cancellationToken).ConfigureAwait(false)).IsReady)
            {
                ready = candidate;
                break;
            }
        }

        if (ready is null)
        {
            return Results.Problem(
                title: "Aucune sauvegarde accessible",
                detail: "Branchez la clé, ou montez le partage qui porte vos documents, puis réessayez.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var restore = new FolderRestore(ready, loggers.CreateLogger<FolderRestore>());

            var outcome = await restore
                .RunAsync(database, input.Ongoing.Trim(), input.Closed.Trim(), null, cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(outcome);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                title: "Impossible d'écrire dans ce dossier",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task UpsertAsync(
        AvocadoDbContext database,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await database.PracticeSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            database.PracticeSettings.Add(new PracticeSetting { Key = key, Value = value });
            return;
        }

        existing.Value = value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
