namespace Avocado.Server.Features.Imports;

/// <param name="SourcePath">Absolute, so the run does not have to re-derive it and cannot drift.</param>
/// <param name="Client">The client's name, taken from the folder. The only name the export gives us.</param>
/// <param name="Name">What the dossier will be called. The client, or the affaire under it.</param>
/// <param name="IsOpen">EN COURS or CLASSES, which is the one piece of state the export does carry.</param>
/// <param name="Files">How many documents will be created, emails included.</param>
/// <param name="Emails">Of which .msg or .eml, which become journal entries rather than plain files.</param>
public sealed record ImportCandidate(
    string SourcePath,
    string Client,
    string Name,
    bool IsOpen,
    int Files,
    int Emails,
    long Bytes);

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
