namespace Avocado.Server.Features.Backups.Infrastructure;

/// <summary>
/// When the documents get copied. Once a day, at an hour she chooses, ten in the evening by default.
///
/// <para><b>Why not continuously, like the rest of the sauvegarde.</b> The database is three hundred
/// kilobytes and snapshotting it every half hour costs nothing, so it keeps doing exactly that. Her
/// documents are twelve gigabytes, and reading them is the one part of a backup that is felt: the
/// disk works, the fans turn, and a file she has open in Word is a file being written to while it is
/// read. Doing that at ten in the evening rather than at eleven in the morning is most of the answer,
/// and it costs a single number in Réglages.</para>
///
/// <para><b>A missed night is caught up, not skipped.</b> If the laptop was closed at ten, the next
/// pass after it opens sees that the last capture is older than the most recent window and runs then.
/// A schedule that silently did nothing because the machine was off at the appointed minute would be
/// worse than no schedule: it would look like it was working.</para>
/// </summary>
/// <param name="Hour">Local, 0 to 23. Her ten in the evening, not UTC's.</param>
public sealed record BackupSchedule(bool IsEnabled, int Hour)
{
    public static BackupSchedule Default { get; } = new(IsEnabled: true, Hour: 22);

    /// <summary>Clamped rather than rejected: a stored 25 should not stop the backups.</summary>
    public int EffectiveHour => Math.Clamp(Hour, 0, 23);

    /// <summary>
    /// The start of the window we are in or have just passed. Everything captured before it is due to
    /// be captured again.
    /// </summary>
    public DateTimeOffset MostRecentWindow(DateTimeOffset localNow)
    {
        var todays = new DateTimeOffset(
            localNow.Year, localNow.Month, localNow.Day, EffectiveHour, 0, 0, localNow.Offset);

        return todays <= localNow ? todays : todays.AddDays(-1);
    }

    public bool IsDue(DateTimeOffset? lastCapturedAt, DateTimeOffset localNow) =>
        IsEnabled && (lastCapturedAt is null || lastCapturedAt < MostRecentWindow(localNow));
}
