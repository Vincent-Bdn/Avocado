namespace Avocado.Server.Features.Backups.ValueObjects;

/// <param name="Status">From the vault's SinkStatus: Ready, Absent, Unreachable, Denied.</param>
/// <param name="Location">Where it turned out to be today. « E:\ », for a key that moves.</param>
/// <param name="Reach">OffMachine, SameMachine or InsideVault. Recomputed on every read, since
/// installing a sync client tomorrow changes the answer for a folder configured today.</param>
/// <param name="ReachDetail">Why, in French. Shown on the row so the distinction is visible.</param>
public sealed record BackupDestinationView(
    Guid Id,
    string Kind,
    string Label,
    string? Path,
    bool IsEnabled,
    string Status,
    string Reach,
    string ReachDetail,
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
/// <param name="HasDestination">Any destination at all, which is what « Sauvegarder maintenant » needs.</param>
/// <param name="HasOffMachineDestination">
/// Whether any of them genuinely leaves this computer. Separate from the above on purpose: a folder
/// beside the vault is a destination worth running and is not safety, and one flag cannot say both.
/// </param>
/// <param name="Exposure">What that instant costs, in work. See <see cref="BackupExposure"/>.</param>
/// <param name="Documents">What the sauvegarde holds of her own folders. See <see cref="CapturedDocuments"/>.</param>
public sealed record BackupStatus(
    DateTimeOffset? ExposedSince,
    DateTimeOffset? LocalSnapshotAt,
    int LocalSnapshotCount,
    bool HasDestination,
    bool HasOffMachineDestination,
    bool AnyReady,
    BackupExposure Exposure,
    CapturedDocuments Documents,
    IReadOnlyList<BackupDestinationView> Destinations);

/// <summary>
/// The documents half, as the Sauvegarde screen needs to say it.
///
/// <para>Its own record because it answers a different question from the rest of the screen. The
/// exposure above is about records, measured in journal entries and hours; this is about files, and
/// the only figures that mean anything to her are how many, how much space, and when they were last
/// read. <see cref="Unreadable"/> is the one that must never be hidden: a document the sauvegarde
/// does not contain is a fact she is entitled to find out on a quiet Tuesday rather than on the day
/// she needs it.</para>
/// </summary>
/// <param name="Dossiers">How many dossiers point at a folder at all. The rest carry no documents.</param>
/// <param name="CapturedAt">When the last nightly pass finished. Null before the first one.</param>
/// <param name="Hour">The hour it runs at, local.</param>
/// <param name="Unreachable">Dossiers whose folder was not there that night, left untouched.</param>
/// <param name="Examples">A handful of the failures, for the screen. <see cref="Unreadable"/> is the count.</param>
/// <param name="Running">
/// Non-null while a capture is under way. Only ever seen on the first night, which is the night it
/// matters: without it, three quarters of an hour of reading looks exactly like nothing happening.
/// </param>
public sealed record CapturedDocuments(
    bool IsEnabled,
    int Hour,
    int Files,
    long Bytes,
    int Dossiers,
    DateTimeOffset? CapturedAt,
    int Unreadable,
    int Unreachable,
    IReadOnlyList<CapturedProblem> Examples,
    Infrastructure.CaptureRunning? Running);

public sealed record CapturedProblem(string Dossier, string Path, string Reason);

/// <summary>
/// What has happened since the last copy left this machine, counted.
///
/// <para>« Votre dernière sauvegarde date du 2 mars » is a date, and a date gets read past. « Depuis,
/// vous avez ajouté 46 entrées de journal et 11 h 20 de temps saisi » is the same fact
/// with its price attached, and it is the sentence that makes someone go and find the USB key. The
/// design asked for it; it needs numbers to say it, so the numbers are computed here rather than
/// approximated in the window.</para>
///
/// <para>Zero everywhere is a real and good answer: nothing has changed, so nothing is at risk, and
/// the screen should say so plainly instead of nagging.</para>
/// </summary>
/// <para>Documents are not counted here. They are copied on their own schedule, once a night rather
/// than continuously, so « depuis la dernière sauvegarde » would be answering a different question
/// about them. <see cref="CapturedDocuments"/> says when they were last read, which is the honest
/// version of the same reassurance.</para>
public sealed record BackupExposure(int Activities, int TimeEntries, int Minutes)
{
    public static BackupExposure None { get; } = new(0, 0, 0);

    public bool IsEmpty => this == None;
}
