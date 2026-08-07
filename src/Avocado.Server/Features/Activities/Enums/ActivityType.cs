namespace Avocado.Server.Features.Activities.Enums;

/// <summary>
/// Direction is folded in rather than carried as a separate field. It is meaningless for a call or a
/// note, but for letters « envoyé le 12/03 » versus « reçu le 15/03 » starts délais and evidences
/// diligence, so it lives where it actually matters. The composer shows these nine as chips.
/// </summary>
public enum ActivityType
{
    /// <summary>Appel.</summary>
    Call,

    /// <summary>Mail reçu.</summary>
    IncomingEmail,

    /// <summary>Mail envoyé.</summary>
    OutgoingEmail,

    /// <summary>Courrier reçu.</summary>
    IncomingLetter,

    /// <summary>Courrier envoyé.</summary>
    OutgoingLetter,

    /// <summary>RDV.</summary>
    Meeting,

    /// <summary>Note.</summary>
    Note,

    /// <summary>Audience.</summary>
    Hearing,

    /// <summary>Autre.</summary>
    Other,
}
