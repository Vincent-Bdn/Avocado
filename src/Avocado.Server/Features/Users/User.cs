namespace Avocado.Server.Features.Users;

/// <summary>
/// Someone who works in the practice. Exactly one today, but the seam matters: it is where the
/// practice's default hourly rate lives, and it lets a journal entry or a time entry record <em>who</em>
///, which is what a second lawyer or a secretary would need, without reshaping the model then.
/// <para>
/// Deliberately not an authentication principal. The vault's device key already decides who may open
/// the application; this is an attribution record, and adding passwords here would be a second, weaker
/// answer to a question the vault has already settled.
/// </para>
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>« M<sup>e</sup> Martine Charpentier ».</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>
    /// The practice default, copied into a matter at creation and frozen there. Changing it affects
    /// new matters only, never the two years of history already priced.
    /// </summary>
    public long HourlyRateCents { get; set; }

    /// <summary>Kept rather than deleted, so their past entries stay attributed.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
