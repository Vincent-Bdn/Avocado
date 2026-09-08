namespace Avocado.Server.Features.Settings;

/// <summary>
/// A key/value row, and deliberately nothing more.
/// <para>
/// Réglages will keep growing, and a typed column per setting means a migration every time she wants
/// to change one number. Keys are namespaced strings (<c>practice.hourlyRateCents</c>) and values are
/// text; the slice that reads a setting is the one that knows how to parse it, and an unknown key is
/// simply absent rather than an error.
/// </para>
/// </summary>
public class PracticeSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PracticeSettingKeys
{
    /// <summary>
    /// The rate a new dossier starts from. It is only ever a starting point: the rate is snapshotted
    /// onto the dossier at creation, so raising it never reprices two years of history.
    /// </summary>
    public const string HourlyRateCents = "practice.hourlyRateCents";

    /// <summary>Default for a solo practice in droit des affaires, and a figure she can change.</summary>
    public const long DefaultHourlyRateCents = 24_000;

    /// <summary>
    /// Her own email addresses, one per line, lower-cased.
    ///
    /// <para>What decides whether a message in the journal reads « reçu » or « envoyé ». Until this
    /// existed every imported message was filed as reçu, 4 713 of them in the real export, because the
    /// direction of a message is whether the sender is her and nothing in the vault said who she
    /// was.</para>
    ///
    /// <para>A list rather than one address, since a practice has an @cabinet and a @gmail and a
    /// secretariat, and a message from any of them was sent by the cabinet.</para>
    /// </summary>
    public const string EmailAddresses = "practice.emailAddresses";

    /// <summary>Whether the sauvegarde carries the dossiers' documents at all. On by default.</summary>
    public const string CaptureDocuments = "backup.documents.enabled";

    /// <summary>The hour, local, at which the nightly copy of the documents starts. 0 to 23.</summary>
    public const string CaptureHour = "backup.documents.hour";

    /// <summary>When the last one finished, so a missed night is caught up rather than skipped.</summary>
    public const string CapturedAt = "backup.documents.capturedAt";

    /// <summary>
    /// What that pass found, as JSON, including the files it could not read.
    ///
    /// <para>Kept in the database rather than in memory on purpose. The whole point of running at ten
    /// in the evening is that nobody is watching, so the report has to survive until morning, and past
    /// a restart on the way.</para>
    /// </summary>
    public const string CaptureReport = "backup.documents.report";
}
