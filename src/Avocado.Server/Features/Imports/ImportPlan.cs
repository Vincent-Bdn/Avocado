namespace Avocado.Server.Features.Imports;

/// <param name="SourcePath">Absolute, so the run does not have to re-derive it and cannot drift.</param>
/// <param name="Client">The client's name, taken from the folder. The only name the export gives us.</param>
/// <param name="Name">What the dossier will be called. The client, or the affaire under it.</param>
/// <param name="IsOpen">EN COURS or CLASSES, which is the one piece of state the export does carry.</param>
/// <param name="Files">How many documents will be created, emails included.</param>
/// <param name="Emails">Of which .msg or .eml, which become journal entries rather than plain files.</param>
/// <param name="Subfolders">
/// How many directories sit at the top of it, so the screen can offer a split on any client rather
/// than only the ones guessed at. JH TRANSPORT is 5,215 files across six affaires and is not
/// suggested, because one of the six is named « 700119 » and a leading digit reads as a filing
/// scheme. The suggestion is a hint; the choice belongs on every row.
/// </param>
public sealed record ImportCandidate(
    string SourcePath,
    string Client,
    string Name,
    bool IsOpen,
    int Files,
    int Emails,
    long Bytes,
    int Subfolders);

/// <param name="Splittable">
/// Client folders holding what look like several affaires. Offered, never applied: see
/// <see cref="Infrastructure.GestisoftScan"/> for why this is a suggestion and not a decision.
/// </param>
public sealed record ImportPlan(
    string Root,
    IReadOnlyList<ImportCandidate> Candidates,
    IReadOnlyList<SplitSuggestion> Splittable,
    IReadOnlyList<string> Skipped)
{
    public int Files => Candidates.Sum(candidate => candidate.Files);
    public long Bytes => Candidates.Sum(candidate => candidate.Bytes);
}

/// <param name="Affaires">What the client folder would become if split.</param>
public sealed record SplitSuggestion(string SourcePath, string Client, IReadOnlyList<string> Affaires);
