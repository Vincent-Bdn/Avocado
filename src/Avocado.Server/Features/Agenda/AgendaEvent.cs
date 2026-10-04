namespace Avocado.Server.Features.Agenda;

/// <summary>
/// One échéance, as it currently stands in her Google agenda.
///
/// <para>The row exists so the push can tell « already there and unchanged » from « never sent » and
/// from « sent, then edited here ». Without it every pass would rewrite every event, which Google
/// rate-limits and which would also overwrite nothing useful several hundred times a day.</para>
///
/// <para><b>It is a mirror, and the mirror is one way.</b> Avocado never reads her agenda back. If she
/// moves the event in Google, the next pass moves it back: the échéance in the dossier is the record,
/// and two systems both allowed to be right is how a lawyer ends up at the wrong tribunal.</para>
/// </summary>
public class AgendaEvent
{
    /// <summary>The échéance this stands for. One event per échéance, so this is the key.</summary>
    public Guid DeadlineId { get; set; }

    /// <summary>Google's id for the event, which is what lets it be updated rather than duplicated.</summary>
    public string GoogleEventId { get; set; } = string.Empty;

    /// <summary>
    /// What was last pushed, hashed. An échéance whose fingerprint still matches is left alone.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
