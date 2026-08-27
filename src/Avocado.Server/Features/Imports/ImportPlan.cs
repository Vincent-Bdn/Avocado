namespace Avocado.Server.Features.Imports;

/// <param name="Path">Absolute, and the identity of the row: what the screen sends back when she marks it.</param>
/// <param name="Client">
/// Whose affaire this is, read from the shape of the tree: the folder's own name near the top, the
/// name of the folder above it further down. Overridden by the contacts list wherever there is one.
/// </param>
/// <param name="Files">Directly inside this folder, nowhere else. The number that says whether marking
/// the parent instead of the children would leave anything behind.</param>
/// <param name="TotalFiles">Everything underneath, which is what gets imported if this is a dossier.</param>
/// <param name="Suggested">
/// What the scan would have chosen on its own. A starting point for the marks and nothing more: the
/// shape of a folder tree does not always say where one affaire ends and the next begins.
/// </param>
public sealed record ImportFolder(
    string Path,
    string Name,
    string Client,
    bool IsOpen,
    int Files,
    int Emails,
    int TotalFiles,
    int TotalEmails,
    long TotalBytes,
    bool Suggested,
    string? GestisoftCode,
    string? ContactsFile,
    string? BillingFile,
    /// <summary>Everything that could be a contacts list, best first, so she can pick another.</summary>
    IReadOnlyList<string> ContactsCandidates,
    /// <summary>Likewise for the billing export.</summary>
    IReadOnlyList<string> BillingCandidates,
    IReadOnlyList<ImportFolder> Children);

/// <param name="Folders">The source tree, in full, for her to walk and mark.</param>
public sealed record ImportPlan(
    string Root,
    IReadOnlyList<ImportFolder> Folders,
    IReadOnlyList<string> Skipped)
{
    public int Files => Folders.Sum(folder => folder.TotalFiles);
    public long Bytes => Folders.Sum(folder => folder.TotalBytes);
}

/// <param name="SourcePath">Absolute, so the run does not have to re-derive it and cannot drift.</param>
/// <param name="Client">The client's name, from the tree or from the contacts list.</param>
/// <param name="Name">What the dossier will be called.</param>
/// <param name="IsOpen">Decided by the path: a folder under CLASSES holds finished work.</param>
/// <param name="Files">How many documents will be created, emails included.</param>
/// <param name="Emails">Of which .msg or .eml, which become journal entries rather than plain files.</param>
/// <param name="GestisoftCode">« 700770 », when the folder is named after it. The one identifier the
/// folder, the contacts list and the billing export all share.</param>
/// <param name="ContactsFile">A contacts PDF found inside, if Gestisoft managed to export one.</param>
/// <param name="BillingFile">The billing spreadsheet, likewise. Most dossiers have neither.</param>
public sealed record ImportCandidate(
    string SourcePath,
    string Client,
    string Name,
    bool IsOpen,
    int Files,
    int Emails,
    long Bytes,
    int Subfolders,
    string? GestisoftCode = null,
    string? ContactsFile = null,
    string? BillingFile = null);
