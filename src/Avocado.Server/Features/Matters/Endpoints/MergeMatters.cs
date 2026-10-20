using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

/// <param name="DocumentsFolder">
/// Which of the two folders the merged dossier keeps. Null keeps the target's, and adopts the
/// source's when the target has none. Anything else is refused: it has to be one of the two, because
/// pointing a merged dossier at a third folder is not a merge.
/// </param>
public sealed record MergeMatterInput(string? DocumentsFolder = null);

/// <param name="Warnings">
/// In French, ready to show. What the confirmation screen says out loud before she presses the
/// button, because this cannot be undone.
/// </param>
public sealed record MergePreview(
    Guid SourceId,
    string SourceReference,
    string SourceName,
    Guid TargetId,
    string TargetReference,
    string TargetName,
    int Activities,
    int TimeEntries,
    int BillableMinutes,
    int Deadlines,
    int Invoices,
    long InvoicedCents,
    int Movements,
    int Costs,
    int Parties,
    int PartiesAlreadyThere,
    string? SourceFolder,
    string? TargetFolder,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Fusionner deux dossiers qui sont le même.
///
/// <para>« Problème avec dossier FINANCIERE CN NIETO / FINANCIERE NIETO, 2 dossiers. Peut-on merger
/// sur FINANCIERE CN (NIETO) ? » It happens when a client is created twice under two spellings, or
/// when an import and a hand-typed dossier meet, and the practice then has one matter's work spread
/// over two: two « reste à facturer », two sets of échéances, and a chiffre d'affaires that is right
/// only if you remember to add them up.</para>
///
/// <para><b>The one that silently corrupts money is the taux horaire.</b> A <c>TimeEntry</c> with no
/// rate of its own is valued at its dossier's rate, and the rate is snapshotted per dossier precisely
/// so that history cannot be repriced. Move such a line to a dossier at a different rate and it
/// quietly becomes worth something else — her chiffre d'affaires moves on its own, retroactively, in
/// a direction nobody chose. So every line that was relying on its dossier's rate has that rate
/// written onto it before it travels. Factures are already safe: <c>BilledTimeCents</c> was frozen on
/// the day each one was issued.</para>
///
/// <para><b>Nothing else is merged.</b> The target keeps its own name, reference, taux, forfait,
/// juridiction and classification: a merge is « these rows belong to that dossier », not a
/// negotiation between two sets of fields, and a rule that filled the target's empty fields from the
/// source would be a second, invisible edit she never asked for. The one exception is the documents
/// folder, because a merged dossier that points at nothing has lost access to its own files.</para>
///
/// <para><b>It cannot be undone</b>, so there is a preview that counts everything first and names
/// what it is worried about.</para>
/// </summary>
public static class MergeMatters
{
    public static async Task<IResult> PreviewAsync(
        Guid id,
        Guid targetId,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var found = await ReadAsync(database, id, targetId, cancellationToken);

        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var (source, target) = (found.Source!, found.Target!);

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.MatterId == id)
            .Select(entry => new { entry.DurationMinutes, entry.IsBillable })
            .ToListAsync(cancellationToken);

        var invoices = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.MatterId == id)
            .Select(invoice => new { invoice.AmountExclVatCents, invoice.IsAbandon })
            .ToListAsync(cancellationToken);

        var sourceParties = await database.MatterParties
            .AsNoTracking()
            .Where(party => party.MatterId == id)
            .Select(party => party.ContactId)
            .ToListAsync(cancellationToken);

        var targetContacts = await database.MatterParties
            .AsNoTracking()
            .Where(party => party.MatterId == targetId)
            .Select(party => party.ContactId)
            .ToListAsync(cancellationToken);

        var warnings = new List<string>();

        if (source.HourlyRateCents != target.HourlyRateCents)
        {
            warnings.Add(
                $"Les taux horaires diffèrent : {Euros(source.HourlyRateCents)} sur {source.Reference}, "
                + $"{Euros(target.HourlyRateCents)} sur {target.Reference}. Le temps passé repris "
                + "gardera son taux d'origine, donc les montants déjà constatés ne changent pas.");
        }

        if (source.FixedFeeCents is not null && target.FixedFeeCents is not null)
        {
            warnings.Add(
                "Les deux dossiers ont un forfait convenu. Seul celui du dossier conservé est gardé : "
                + "vérifiez-le après la fusion.");
        }

        if (source.DocumentsFolder is { Length: > 0 } && target.DocumentsFolder is { Length: > 0 }
            && !string.Equals(source.DocumentsFolder, target.DocumentsFolder, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Les deux dossiers pointent vers des répertoires différents. Choisissez lequel garder ; "
                + "aucun fichier n'est déplacé.");
        }

        if (source.ClosedOn is not null != (target.ClosedOn is not null))
        {
            warnings.Add("L'un des deux dossiers est clôturé et l'autre pas. Le dossier conservé garde son état.");
        }

        return Results.Ok(new MergePreview(
            source.Id, source.Reference, source.Name,
            target.Id, target.Reference, target.Name,
            await database.Activities.CountAsync(activity => activity.MatterId == id, cancellationToken),
            entries.Count,
            entries.Where(entry => entry.IsBillable).Sum(entry => entry.DurationMinutes),
            await database.Deadlines.CountAsync(deadline => deadline.MatterId == id, cancellationToken),
            invoices.Count,
            invoices.Where(invoice => !invoice.IsAbandon).Sum(invoice => invoice.AmountExclVatCents),
            await database.LedgerEntries.CountAsync(entry => entry.MatterId == id, cancellationToken),
            await database.Costs.CountAsync(cost => cost.MatterId == id, cancellationToken),
            sourceParties.Count,
            sourceParties.Count(contact => targetContacts.Contains(contact)),
            source.DocumentsFolder,
            target.DocumentsFolder,
            warnings));
    }

    public static async Task<IResult> HandleAsync(
        Guid id,
        Guid targetId,
        MergeMatterInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var found = await ReadAsync(database, id, targetId, cancellationToken);

        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var (source, target) = (found.Source!, found.Target!);

        var folder = Folder(input.DocumentsFolder, source, target);

        if (folder.Problem is not null)
        {
            return folder.Problem;
        }

        // The rate first, before anything moves. A line relying on its dossier's rate is about to be
        // valued by a different dossier, and that would reprice history in a direction nobody chose.
        var entries = await database.TimeEntries
            .Where(entry => entry.MatterId == id)
            .ToListAsync(cancellationToken);

        if (source.HourlyRateCents != target.HourlyRateCents)
        {
            foreach (var entry in entries.Where(entry => entry.HourlyRateCentsOverride is null))
            {
                entry.HourlyRateCentsOverride = source.HourlyRateCents;
            }
        }

        foreach (var entry in entries)
        {
            entry.MatterId = targetId;
        }

        await MoveAsync(database.Activities, activity => activity.MatterId == id, cancellationToken,
            activity => activity.MatterId = targetId);

        await MoveAsync(database.Deadlines, deadline => deadline.MatterId == id, cancellationToken,
            deadline => deadline.MatterId = targetId);

        await MoveAsync(database.Invoices, invoice => invoice.MatterId == id, cancellationToken,
            invoice => invoice.MatterId = targetId);

        await MoveAsync(database.LedgerEntries, entry => entry.MatterId == id, cancellationToken,
            entry => entry.MatterId = targetId);

        await MoveAsync(database.Costs, cost => cost.MatterId == id, cancellationToken,
            cost => cost.MatterId = targetId);

        await MergePartiesAsync(database, id, targetId, cancellationToken);
        await MergeCapturedFilesAsync(database, id, targetId, folder.Path, source, cancellationToken);

        target.DocumentsFolder = folder.Path;

        // Everything that pointed at the source now points at the target, so the row itself goes.
        // Leaving it behind, closed, would keep a dossier in her lists that holds nothing and is
        // the same affair as another one, which is the state she asked to get out of.
        database.Matters.Remove(source);

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { mergedInto = targetId, reference = target.Reference });
    }

    /// <summary>
    /// Both dossiers, or the reason there is nothing to do.
    ///
    /// <para>A dossier cannot be merged into itself, which is what a double-click on the confirm
    /// button would otherwise do to the whole of its own content.</para>
    /// </summary>
    private static async Task<(Matter? Source, Matter? Target, IResult? Problem)> ReadAsync(
        AvocadoDbContext database,
        Guid id,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        if (id == targetId)
        {
            return (null, null, Problem("Un dossier ne peut pas être fusionné avec lui-même."));
        }

        var source = await database.Matters.FirstOrDefaultAsync(matter => matter.Id == id, cancellationToken);
        var target = await database.Matters.FirstOrDefaultAsync(matter => matter.Id == targetId, cancellationToken);

        if (source is null || target is null)
        {
            return (null, null, Results.NotFound());
        }

        return (source, target, null);
    }

    /// <summary>
    /// Which folder the merged dossier points at.
    ///
    /// <para>The target's, unless it has none and the source does — a merged dossier that points at
    /// nothing has lost access to its own documents, which is not a tidy-up anyone wanted. She may
    /// name the other one explicitly; a third path is refused, because pointing a merged dossier
    /// somewhere neither of them was is not a merge.</para>
    /// </summary>
    private static (string? Path, IResult? Problem) Folder(string? chosen, Matter source, Matter target)
    {
        if (chosen is null or "")
        {
            return (target.DocumentsFolder is { Length: > 0 }
                ? target.DocumentsFolder
                : source.DocumentsFolder, null);
        }

        if (Same(chosen, source.DocumentsFolder) || Same(chosen, target.DocumentsFolder))
        {
            return (chosen, null);
        }

        return (null, Problem(
            "Le répertoire à conserver doit être celui de l'un des deux dossiers."));
    }

    private static bool Same(string left, string? right) =>
        right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The parties, without creating a duplicate of a contact the target already carries.
    ///
    /// <para>A contact already on the target keeps the target's row exactly as it is, and the source's
    /// is dropped: two rows for the same tiers on one dossier is a fiche that lists its client twice.
    /// One exception — when the target has no client at all and the source's row was the client, the
    /// target's row for that contact becomes the client, because a dossier whose client is nobody is
    /// a dossier nothing can be billed on.</para>
    /// </summary>
    private static async Task MergePartiesAsync(
        AvocadoDbContext database,
        Guid id,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        var moving = await database.MatterParties
            .Where(party => party.MatterId == id)
            .ToListAsync(cancellationToken);

        var existing = await database.MatterParties
            .Where(party => party.MatterId == targetId)
            .ToListAsync(cancellationToken);

        var targetHasClient = existing.Any(party => party.IsClient);

        foreach (var party in moving)
        {
            var already = existing.FirstOrDefault(other => other.ContactId == party.ContactId);

            if (already is null)
            {
                party.MatterId = targetId;

                if (party.IsClient)
                {
                    targetHasClient = true;
                }

                continue;
            }

            if (party.IsClient && !targetHasClient)
            {
                already.IsClient = true;
                targetHasClient = true;
            }

            database.MatterParties.Remove(party);
        }
    }

    /// <summary>
    /// The sauvegarde's manifest follows the folder that is kept, and the other side's rows go.
    ///
    /// <para>A row here is a photograph of a file in a dossier's folder, named by a path relative to
    /// it. Keep the rows describing a folder the merged dossier no longer points at and the manifest
    /// claims files that are not there; the next capture would notice, but until then the backup
    /// screen would be describing a dossier that does not exist. Nothing restorable is lost either
    /// way: the blobs are content-addressed, and every past snapshot carries its own copy of this
    /// table taken at the same instant.</para>
    ///
    /// <para><b>Tracked, not <c>ExecuteDelete</c>.</b> The bulk operators issue their statement
    /// immediately and outside the change tracker, so they commit whatever happens next: a merge that
    /// then failed to save would roll every row back except the manifest, which would already have
    /// been destroyed. One <c>SaveChangesAsync</c> does all of this or none of it, and that has to
    /// include this.</para>
    /// </summary>
    private static async Task MergeCapturedFilesAsync(
        AvocadoDbContext database,
        Guid id,
        Guid targetId,
        string? kept,
        Matter source,
        CancellationToken cancellationToken)
    {
        var keepingTheSourceFolder = kept is not null && Same(kept, source.DocumentsFolder);

        var mine = await database.CapturedFiles
            .Where(file => file.MatterId == id)
            .ToListAsync(cancellationToken);

        if (!keepingTheSourceFolder)
        {
            database.CapturedFiles.RemoveRange(mine);
            return;
        }

        database.CapturedFiles.RemoveRange(await database.CapturedFiles
            .Where(file => file.MatterId == targetId)
            .ToListAsync(cancellationToken));

        foreach (var file in mine)
        {
            file.MatterId = targetId;
        }
    }

    /// <summary>
    /// Loaded and reassigned rather than updated in bulk, so the change travels through the change
    /// tracker with everything else and one <c>SaveChangesAsync</c> either does all of this or none
    /// of it. A half-merged dossier is the one outcome there is no way back from.
    /// </summary>
    private static async Task MoveAsync<T>(
        DbSet<T> set,
        System.Linq.Expressions.Expression<Func<T, bool>> mine,
        CancellationToken cancellationToken,
        Action<T> reassign) where T : class
    {
        foreach (var row in await set.Where(mine).ToListAsync(cancellationToken))
        {
            reassign(row);
        }
    }

    private static string Euros(long cents) => $"{cents / 100d:0.##} €";

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Fusion", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
