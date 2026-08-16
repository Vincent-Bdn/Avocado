namespace Avocado.Server.Features.Documents.Checkout;

/// <summary>
/// A dossier whose documents are currently decrypted into a folder she can work in.
///
/// <para>Persisted rather than held in memory, and that is the whole point: after a crash this row is
/// the only record of what was handed over, and without it the folder on disk is an unreadable pile
/// that could only be swept. See <see cref="CheckoutResumptionCheck"/> for what it is compared
/// against.</para>
///
/// <para>Several may be open at once. Forcing one at a time would mean closing a dossier to spend five
/// minutes in another and then reopening it, which is the kind of bookkeeping that makes people leave
/// everything open anyway.</para>
/// </summary>
public class MatterCheckout
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }

    /// <summary>Absolute, and machine-local. It lives under the working directory, never in the vault.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// What was written, as JSON: one entry per file with its document id and plaintext hash. Compared
    /// to the folder to work out what changed, both continuously and at the next launch.
    /// </summary>
    public string Manifest { get; set; } = "[]";

    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last time the folder and the vault were reconciled, for the screen.</summary>
    public DateTimeOffset? SyncedAt { get; set; }
}
