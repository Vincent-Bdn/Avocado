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

    /// <summary>
    /// The first day of the exercice comptable, as « MM-DD ». « 09-01 » for a practice that opened in
    /// September and closes its books in August.
    ///
    /// <para>A setting rather than a constant because it is the one figure the chiffre d'affaires is
    /// counted against, and it differs per cabinet. Absent, the exercice is the calendar year.</para>
    /// </summary>
    public const string ExerciceStart = "practice.exerciceStart";

    /// <summary>
    /// The cabinet's stamp, as « sha256:size » pointing into the coffre's blobs.
    ///
    /// <para>In the coffre rather than beside it: it is encrypted, it goes into the sauvegarde, and
    /// it comes back on a new machine. Most cabinets have one copy of that graphic and could not
    /// redraw it.</para>
    /// </summary>
    public const string StampImage = "practice.stampImage";

    /// <summary>Where the number sits on that image, and which corner of the page. JSON.</summary>
    public const string StampOptions = "practice.stampOptions";

    /// <summary>
    /// Her stamp as an SVG, when she gave one, kept as text.
    ///
    /// <para>The image above is still what gets drawn onto a pièce: this is the source it was drawn
    /// from. Keeping it means the placeholder can be found again when she changes the stamp's size,
    /// and that replacing her logo is editing a file rather than starting over.</para>
    ///
    /// <para>Small enough for a settings row. A logo SVG is a few kilobytes of path data; the one
    /// thing that would not be is an SVG with a photograph embedded in it, and that is not a
    /// stamp.</para>
    /// </summary>
    public const string StampSvg = "practice.stampSvg";

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
