using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Matters;

namespace Avocado.Server.Features.Activities;

/// <summary>
/// One event in a matter's chronology — « le suivi ». Adding one must be the fastest interaction in
/// the application.
/// </summary>
public class Activity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    /// <summary>
    /// When it happened, not when it was typed — the composer's timestamp is editable and pre-filled
    /// with now, because the 11:00 call is usually logged at 17:00.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public ActivityType Type { get; set; }

    /// <summary>Who it was with, when that is known. Their role comes from the matter, not from here.</summary>
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    /// <summary>
    /// Numéro de suivi for a recommandé or a courrier tracked by the poste. Only ever set on the two
    /// letter types; the timeline renders it beside the type name.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>Who recorded it. Null on entries written before the practice had named users.</summary>
    public Guid? UserId { get; set; }
    public Users.User? User { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
