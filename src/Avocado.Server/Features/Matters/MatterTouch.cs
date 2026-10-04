namespace Avocado.Server.Features.Matters;

/// <summary>
/// The latest of everything recorded against a dossier.
///
/// <para>It used to order « dossiers récemment touchés » on the accueil. That list is « dossiers
/// consultés » now, ordered by <see cref="Matter.LastViewedAt"/>; this remains the fallback for a
/// dossier she has not opened since views were recorded, and the answer to « when did anything last
/// happen here » wherever that is the question.</para>
/// <para>
/// An afternoon spent entering time and recording a provision leaves no journal entry, and a dossier
/// that vanishes from the list on the day you actually worked on it teaches people not to trust the
/// list. So the recency is the latest of five things: a journal entry, a document, a time entry, an
/// invoice and a mouvement.
/// </para>
/// <para>
/// The five are read as separate scalar subqueries and combined here rather than in SQL. SQLite has
/// no clean way to take the max of five correlated subqueries, and the alternative, a UNION inside a
/// projection, is exactly the shape EF gives up on. The comparison itself is trivial; doing it in
/// memory costs one pass over a page of rows.
/// </para>
/// </summary>
public static class MatterTouch
{
    public static DateTimeOffset? Latest(params DateTimeOffset?[] candidates) =>
        candidates.Where(candidate => candidate.HasValue).DefaultIfEmpty(null).Max();
}
