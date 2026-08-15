namespace Avocado.Server.Features.Backups.ValueObjects;

/// <param name="Status">From the vault's SinkStatus: Ready, Absent, Unreachable, Denied.</param>
/// <param name="Location">Where it turned out to be today. « E:\ », for a key that moves.</param>
public sealed record BackupDestinationView(
    Guid Id,
    string Kind,
    string Label,
    string? Path,
    bool IsEnabled,
    string Status,
    string? Location,
    DateTimeOffset? LastBackupAt,
    string? LastError,
    int KeepNewest,
    int KeepDailyForDays);

/// <summary>
/// The answer to the only question that matters, computed in one place so that every screen showing
/// it shows the same thing.
///
/// <para><see cref="ExposedSince"/> is the whole point: the instant of the newest copy that is not on
/// this machine. Everything before it survives the laptop being dropped in the Seine; everything
/// after it does not. A count of destinations and a list of green ticks do not answer that, and a
/// backup screen that does not answer it is decoration.</para>
/// </summary>
/// <param name="ExposedSince">Null when nothing has ever left this machine, which is its own answer.</param>
/// <param name="LocalSnapshotAt">The newest local snapshot. History, not safety: it dies with the disk.</param>
/// <param name="Exposure">What that instant costs, in work. See <see cref="BackupExposure"/>.</param>
public sealed record BackupStatus(
    DateTimeOffset? ExposedSince,
    DateTimeOffset? LocalSnapshotAt,
    int LocalSnapshotCount,
    bool HasDestination,
    bool AnyReady,
    BackupExposure Exposure,
    IReadOnlyList<BackupDestinationView> Destinations);

/// <summary>
/// What has happened since the last copy left this machine, counted.
///
/// <para>« Votre dernière sauvegarde date du 2 mars » is a date, and a date gets read past. « Depuis,
/// vous avez ajouté 46 entrées de journal, 9 documents et 11 h 20 de temps saisi » is the same fact
/// with its price attached, and it is the sentence that makes someone go and find the USB key. The
/// design asked for it; it needs numbers to say it, so the numbers are computed here rather than
/// approximated in the window.</para>
///
/// <para>Zero everywhere is a real and good answer: nothing has changed, so nothing is at risk, and
/// the screen should say so plainly instead of nagging.</para>
/// </summary>
public sealed record BackupExposure(int Activities, int Documents, int TimeEntries, int Minutes)
{
    public static BackupExposure None { get; } = new(0, 0, 0, 0);

    public bool IsEmpty => this == None;
}
