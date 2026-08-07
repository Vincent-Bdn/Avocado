using Avocado.Server.Features.Contacts;

namespace Avocado.Server.Features.Matters;

/// <summary>
/// Links a contact to a matter. <see cref="Role"/> is free text so a new kind of party never needs a
/// release; <see cref="IsClient"/> stays structural because "who is this matter for" and "who do I
/// bill" have to be answerable.
/// </summary>
public class MatterParty
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }
    public Matter? Matter { get; set; }

    public Guid ContactId { get; set; }
    public Contact? Contact { get; set; }

    public bool IsClient { get; set; }

    /// <summary>
    /// « Partie adverse », « Avocat de la partie adverse au barreau de Villefranche », « Expert
    /// judiciaire désigné par ordonnance du 12/01/2026 ». Long by nature, the UI truncates and shows
    /// the full text on hover, so do not constrain it to a short list.
    /// </summary>
    public string? Role { get; set; }
}
