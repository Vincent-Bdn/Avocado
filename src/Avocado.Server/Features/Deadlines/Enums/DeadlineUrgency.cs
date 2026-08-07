namespace Avocado.Server.Features.Deadlines.Enums;

/// <summary>
/// The four tiers the liste des dossiers, the accueil and the ⌘K palette all render. Computed on the
/// server so the three screens can never disagree about what "cette semaine" means; each has its own
/// bullet shape in the design, so colour is never the only signal.
/// </summary>
public enum DeadlineUrgency
{
    /// <summary>Dépassée.</summary>
    Overdue,

    /// <summary>Aujourd'hui.</summary>
    Today,

    /// <summary>Cette semaine, within the next 7 days.</summary>
    ThisWeek,

    /// <summary>Plus tard.</summary>
    Later,
}
