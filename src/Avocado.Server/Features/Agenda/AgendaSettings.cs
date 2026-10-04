namespace Avocado.Server.Features.Agenda;

/// <summary>
/// Where the agenda connection lives: settings rows, in the vault database, which is SQLCipher.
///
/// <para>A refresh token is a key to her calendar. It sits in the encrypted database for the same
/// reason a backup destination's secret does, and never in a config file beside it.</para>
/// </summary>
public static class AgendaSettingKeys
{
    /// <summary>Off keeps the connection and stops the pushing, which is what « pause » means.</summary>
    public const string Enabled = "agenda.google.enabled";

    /// <summary>
    /// The OAuth client this Avocado authenticates as, as issued by a Google Cloud project.
    ///
    /// <para><b>Not shipped with the application, and here rather than compiled in.</b> Avocado is
    /// self-hosted and its source is public, so a client secret baked into it would be a published
    /// secret. More to the point, the consent screen a lawyer sees carries the legal identity of
    /// whoever owns that project, and a cabinet connecting its own agenda is entitled to know whose
    /// name that is. A practice that does not want to create one simply does not use this.</para>
    ///
    /// <para>Read from configuration first, so whoever distributes a build can provide one for the
    /// cabinets they support; these rows are the fallback and what Réglages writes.</para>
    /// </summary>
    public const string ClientId = "agenda.google.clientId";

    /// <summary>Google calls it a secret; for an installed application it is not one. Encrypted anyway.</summary>
    public const string ClientSecret = "agenda.google.clientSecret";

    /// <summary>The long-lived grant. Everything else is derived from it and nothing else is kept.</summary>
    public const string RefreshToken = "agenda.google.refreshToken";

    /// <summary>Which calendar the échéances go into. Avocado makes its own and never writes elsewhere.</summary>
    public const string CalendarId = "agenda.google.calendarId";

    /// <summary>The account it is connected to, shown in Réglages so « connecté » names someone.</summary>
    public const string Account = "agenda.google.account";

    /// <summary>When a pass last finished with nothing left to send.</summary>
    public const string SyncedAt = "agenda.google.syncedAt";

    /// <summary>
    /// What went wrong, in French, or absent. Kept in the database rather than in memory for the
    /// reason the backup report is: a failure at two in the morning has to survive until she looks.
    /// </summary>
    public const string Error = "agenda.google.error";

    /// <summary>
    /// How many passes in a row have failed. Five, then it stops trying and says so, the same policy
    /// as a NAS that is not mounted: a loop retrying for ever burns the quota and tells nobody.
    /// </summary>
    public const string FailureCount = "agenda.google.failures";

    /// <summary>The name Avocado gives the calendar it creates.</summary>
    public const string CalendarName = "Avocado · Échéances";

    /// <summary>Stop trying after this many consecutive failures, and wait for her to press Réessayer.</summary>
    public const int MaxFailures = 5;
}
