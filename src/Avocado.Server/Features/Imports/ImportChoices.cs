namespace Avocado.Server.Features.Imports;

/// <summary>
/// Where she disagreed with what the scan read off the folder tree.
///
/// <para>Only the disagreements. « Under CLASSES means closed » is right for almost all of hers and
/// wrong for a few, and « the PDF whose name begins with contact » picks the list in seven dossiers
/// and a letter in an eighth. Sending the whole state back would work equally well and would mean the
/// screen and the scan could drift apart without anyone noticing; sending only the corrections keeps
/// one source for everything nobody corrected.</para>
/// </summary>
/// <param name="Open">
/// Marked dossiers she says are still en cours, whatever the path says. Null means she said nothing,
/// which is not the same as an empty list: empty means every dossier is closed.
/// </param>
/// <param name="Closed">Marked dossiers she says are finished, likewise.</param>
/// <param name="Contacts">Path of the dossier, then the file she picked for its tiers. Empty clears it.</param>
/// <param name="Billing">The same for its facturation.</param>
public sealed record ImportChoices(
    IReadOnlyList<string>? Open = null,
    IReadOnlyList<string>? Closed = null,
    IReadOnlyDictionary<string, string>? Contacts = null,
    IReadOnlyDictionary<string, string>? Billing = null)
{
    public bool IsOpen(ImportFolder folder)
    {
        if (Has(Open, folder.Path))
        {
            return true;
        }

        return !Has(Closed, folder.Path) && folder.IsOpen;
    }

    /// <summary>
    /// The file she picked, the scan's own guess if she picked none, or nothing if she cleared it.
    /// An empty string is how a form says « none », and it has to survive the round trip as one.
    /// </summary>
    public string? ContactsFor(ImportFolder folder) => Pick(Contacts, folder.Path, folder.ContactsFile);

    public string? BillingFor(ImportFolder folder) => Pick(Billing, folder.Path, folder.BillingFile);

    private static bool Has(IReadOnlyList<string>? paths, string path) =>
        paths is not null && paths.Any(entry => entry.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static string? Pick(IReadOnlyDictionary<string, string>? picked, string path, string? guessed)
    {
        if (picked is null)
        {
            return guessed;
        }

        foreach (var (key, value) in picked)
        {
            if (key.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return guessed;
    }
}
