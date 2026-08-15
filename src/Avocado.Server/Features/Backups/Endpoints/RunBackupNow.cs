namespace Avocado.Server.Features.Backups.Endpoints;

/// <summary>
/// « Sauvegarder maintenant ». Runs the same pass the timer runs, forced, so what the button does and
/// what happens on its own can never drift apart.
/// </summary>
public static class RunBackupNow
{
    public static async Task<IResult> HandleAsync(
        BackupService backups,
        CancellationToken cancellationToken)
    {
        var outcomes = await backups.RunNowAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(outcomes);
    }
}
