using Avocado.Server.Data;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Settings;
using Avocado.Vault;
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

        var vault = vaults.Get(Guid.Empty);

        try
        {
            var restore = new FolderRestore(vault.Blobs, loggers.CreateLogger<FolderRestore>());

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
