using Avocado.Server.Data;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class GetMatter
{
    /// <summary>
    /// How stale a recorded view has to be before opening the fiche writes another one.
    ///
    /// <para>The fiche refetches whenever anything on it changes, so recording every read would write
    /// a row per keystroke in the journal. A minute is finer than « dossiers consultés » can show and
    /// coarse enough that an afternoon on one dossier is a handful of writes.</para>
    /// </summary>
    private static readonly TimeSpan ViewGranularity = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Opening a fiche is what « consulter un dossier » means, so this is where it is recorded.
    ///
    /// <para>A write on a GET, deliberately. The alternative is asking the client to announce it, and
    /// a client that forgets, or a second client that never knew, silently stops the list working.
    /// Reading the fiche is the event; nothing else has to remember to say so.</para>
    /// </summary>
    private static async Task RecordViewAsync(
        AvocadoDbContext database,
        Matter matter,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (matter.LastViewedAt is { } last && now - last < ViewGranularity)
        {
            return;
        }

        // The matter above is AsNoTracking, for the Include that follows it. One targeted update
        // rather than loading it a second time, and UpdatedAt is left alone: she consulted the
        // dossier, she did not change it.
        await database.Matters
            .Where(candidate => candidate.Id == matter.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(candidate => candidate.LastViewedAt, now),
                cancellationToken);

        matter.LastViewedAt = now;
    }

    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        await RecordViewAsync(database, matter, clock, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Closing hides future échéances; it never deletes them, so reopening restores the list.
        var deadlines = matter.IsOpen
            ? await database.Deadlines
                .AsNoTracking()
                .Where(deadline => deadline.MatterId == id && !deadline.IsDone)
                .OrderBy(deadline => deadline.Date)
                .Select(deadline => new MatterDeadlineItem(
                    deadline.Id, deadline.Date, deadline.Time, deadline.Label, default))
                .ToListAsync(cancellationToken)
            : [];

        // « Derniers modifiés » and the count, read from her folder in one walk. A dossier with no
        // folder yet simply has none of either, which is what the aperçu says.
        var (fileCount, recent) = DossierFolderReader.Summarise(matter.DocumentsFolder, 5, cancellationToken);

        var documents = recent
            .Select(entry => new MatterDocumentItem(
                entry.Name, entry.RelativePath, entry.ExhibitNumber, entry.ModifiedAt))
            .ToList();

        var counts = new MatterCounts(
            await database.Activities.CountAsync(activity => activity.MatterId == id, cancellationToken),
            fileCount,
            await database.Deadlines.CountAsync(
                deadline => deadline.MatterId == id && !deadline.IsDone, cancellationToken),
            await database.TimeEntries.CountAsync(entry => entry.MatterId == id, cancellationToken));

        var lastActivity = await database.Activities
            .AsNoTracking()
            .Where(activity => activity.MatterId == id)
            .OrderByDescending(activity => activity.OccurredAt)
            .Select(activity => new MatterLastActivity(
                activity.Type,
                activity.OccurredAt,
                activity.Subject ?? activity.Body))
            .FirstOrDefaultAsync(cancellationToken);

        var detail = new MatterDetail(
            matter.Id,
            matter.Reference,
            matter.Name,
            matter.Description,
            matter.OpenedOn,
            matter.ClosedOn,
            matter.HourlyRateCents,
            matter.FixedFeeCents,
            matter.CourtCaseNumber,
            matter.Classification,
            matter.Court,
            matter.DocumentsFolder,
            matter.IsOpen,
            matter.IsFavourite,
            [.. matter.Parties.Select(party => new MatterPartyItem(
                party.Id,
                party.ContactId,
                party.Contact!.Type,
                party.Contact.Type == ContactType.Organisation
                    ? party.Contact.LegalName ?? string.Empty
                    : $"{party.Contact.FirstName} {party.Contact.LastName}".Trim(),
                party.IsClient,
                party.Role))],
            [.. deadlines.Select(deadline => deadline with
            {
                Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
            })],
            documents,
            counts,
            await BillingSummaryQuery.ForMatterAsync(database, id, cancellationToken),
            lastActivity);

        return Results.Ok(detail);
    }
}
