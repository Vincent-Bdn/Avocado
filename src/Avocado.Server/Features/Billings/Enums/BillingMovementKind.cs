namespace Avocado.Server.Features.Billings.Enums;

/// <summary>
/// What a ledger entry <em>is</em>, chosen before any amount is typed. The stored amount's sign is
/// derived from this and never sent by the client: a débours entered as a positive number would
/// silently corrupt every balance on the dossier, and it is the kind of error found a year later.
/// </summary>
public enum BillingMovementKind
{
    /// <summary>Encaissement, argent reçu du client (provision, acompte, règlement). Stored positive.</summary>
    Receipt,

    /// <summary>Débours, argent avancé pour le client (greffe, expert, huissier). Stored negative.</summary>
    Disbursement,
}
