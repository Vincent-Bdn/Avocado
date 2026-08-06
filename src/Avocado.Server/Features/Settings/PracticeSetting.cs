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
}
