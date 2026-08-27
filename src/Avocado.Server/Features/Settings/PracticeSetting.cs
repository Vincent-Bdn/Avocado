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
}
