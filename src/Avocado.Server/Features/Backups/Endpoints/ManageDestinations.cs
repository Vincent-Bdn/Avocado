using Avocado.Server.Data;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Backups;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Backups.Endpoints;

/// <param name="Kind">« folder » or « volume ». See BackupDestinationKinds.</param>
/// <param name="Path">The folder, for both kinds: for a volume it is where the marker gets written.</param>
/// <param name="AcceptSameMachine">
/// Set by the window once the user has been shown that this folder never leaves the computer and has
/// said to use it anyway. Deliberately not persisted: it records a conversation that happened, not a
/// property of the destination, and the destination's real reach is recomputed on every read because
/// installing OneDrive tomorrow changes the answer.
/// </param>
public sealed record BackupDestinationInput(
    string Kind,
    string Label,
    string? Path,
    bool IsEnabled = true,
    int KeepNewest = 12,
    int KeepDailyForDays = 60,
    bool AcceptSameMachine = false)
{
    public string? Validate() => this switch
    {
        { Label: var label } when string.IsNullOrWhiteSpace(label) => "Donnez un nom à cette destination.",
        { Path: var path } when string.IsNullOrWhiteSpace(path) => "Choisissez un dossier.",
        { KeepNewest: < 1 } => "Il faut conserver au moins une sauvegarde.",
        _ => null,
    };
}

public static class ManageDestinations
{
    /// <summary>
    /// Adds a destination and immediately proves it works, because a backup destination that turns out
    /// to be unwritable is discovered either now, in a dialog, or in a year, on a bad day.
    ///
    /// <para>A removable device also gets its marker written now. That is what lets it be recognised
    /// tomorrow when the operating system has decided it is F:\ rather than E:\.</para>
    /// </summary>
    public static async Task<IResult> AddAsync(
        BackupDestinationInput input,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["destination"] = [error] });
        }

        // A volume reached this far only by being enumerated as removable or network, so it is
        // off-machine by construction. A folder is whatever the user typed.
        if (input.Kind == BackupDestinationKinds.Folder)
        {
            var verdict = DestinationReachInspector.Inspect(input.Path!, vaults.Get(Guid.Empty).Paths.Root);

            // Refused outright, and only this one. There is no arrangement in which a copy stored
            // inside the thing it protects survives anything.
            if (verdict.Reach is DestinationReach.InsideVault)
            {
                return Results.Problem(
                    title: "Ce dossier est dans le coffre",
                    detail: verdict.Detail,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "inside-vault" });
            }

            // A warning, never a refusal: someone may well have a robocopy task, a Time Machine
            // volume or a corporate agent that we have no way of seeing.
            // Its own code, so the window can render the question and the override in French rather
            // than pattern-matching a sentence. Same arrangement as the wizard's synced-folder refusal.
            if (verdict.Reach is DestinationReach.SameMachine && !input.AcceptSameMachine)
            {
                return Results.Problem(
                    title: "Ce dossier ne quitte pas cet ordinateur",
                    detail: verdict.Detail,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = "same-machine" });
            }
        }

        var destination = new BackupDestination
        {
            Kind = input.Kind,
            Label = input.Label.Trim(),
            Path = input.Path!.Trim(),
            IsEnabled = input.IsEnabled,
            KeepNewest = input.KeepNewest,
            KeepDailyForDays = input.KeepDailyForDays,
        };

        if (input.Kind == BackupDestinationKinds.Volume)
        {
            try
            {
                destination.VolumeId = SinkMarker.Write(destination.Path, destination.Label).SinkId;

                // The path was only ever the way to find it the first time. From here on the marker is
                // the identity, and keeping a stale letter would invite writing to whatever takes it.
                destination.Path = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["destination"] = [$"Impossible d'écrire sur ce support : {exception.Message}"],
                });
            }
        }

        database.Set<BackupDestination>().Add(destination);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/backups/destinations/{destination.Id}", new { destination.Id });
    }

    public static async Task<IResult> UpdateAsync(
        Guid id,
        BackupDestinationInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var destination = await database.Set<BackupDestination>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (destination is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(input.Label))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["destination"] = ["Donnez un nom à cette destination."],
            });
        }

        destination.Label = input.Label.Trim();
        destination.IsEnabled = input.IsEnabled;
        destination.KeepNewest = Math.Max(1, input.KeepNewest);
        destination.KeepDailyForDays = Math.Max(0, input.KeepDailyForDays);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// Forgets a destination. What is already on it is deliberately left alone: someone removing a
    /// destination is reorganising, not asking for their only off-machine copy to be erased, and the
    /// files are readable by any future restore.
    /// </summary>
    public static async Task<IResult> RemoveAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var destination = await database.Set<BackupDestination>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (destination is null)
        {
            return Results.NoContent();
        }

        database.Set<BackupDestination>().Remove(destination);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }
}
