using Avocado.Server.Data;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Avocado.Server.Features.Deadlines;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Dashboards.Endpoints;

/// <summary>
/// The accueil: what falls due, what has been earned and not billed, and where she left off.
/// <para>
/// Nothing about backups. The design's status bar and stale-backup banner are out of v1 — the vault
/// knows its own backup history and that surface can be added later without touching this.
/// </para>
/// </summary>
public static class GetDashboard
{
    private const int RecentMatterCount = 8;
    private const int AgeingDays = 60;

    public static async Task<IResult> HandleAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var horizon = today.AddDays(DeadlineUrgencyRule.UpcomingDays);

        var openDeadlines = database.Deadlines
            .AsNoTracking()
            .Where(deadline => !deadline.IsDone && deadline.Matter!.ClosedOn == null);

        var deadlines = await openDeadlines
            .Where(deadline => deadline.Date <= horizon)
            .OrderBy(deadline => deadline.Date)
            .Select(deadline => new DashboardDeadline(
                deadline.Id,
                deadline.MatterId,
                deadline.Matter!.Reference,
                deadline.Matter.Name,
                ClientNameOf(deadline.Matter),
                deadline.Label,
                deadline.Date,
                deadline.Time,
                default))
            .ToListAsync(cancellationToken);

        var nextBeyond = await openDeadlines
            .Where(deadline => deadline.Date > horizon)
            .OrderBy(deadline => deadline.Date)
            .Select(deadline => (DateOnly?)deadline.Date)
            .FirstOrDefaultAsync(cancellationToken);

        var unbilled = await ComputeUnbilledAsync(database, today, cancellationToken);

        // « Touché » is every kind of work, not only the journal: an afternoon spent entering time
        // and recording a provision has to bring its dossier to the top. The five timestamps come
        // back as separate columns and are combined in memory — see MatterTouch.
        var touched = await database.Matters
            .AsNoTracking()
            .Where(matter => matter.ClosedOn == null)
            .Select(matter => new
            {
                matter.Id,
                matter.Reference,
                matter.Name,
                ClientName = ClientNameOf(matter),
                Last = database.Activities
                    .Where(activity => activity.MatterId == matter.Id)
                    .OrderByDescending(activity => activity.OccurredAt)
                    .Select(activity => new
                    {
                        activity.Type,
                        activity.OccurredAt,
                        Summary = activity.Subject ?? activity.Body,
                    })
                    .FirstOrDefault(),
                LastDocumentAt = database.Documents
                    .Where(document => document.MatterId == matter.Id)
                    .Max(document => (DateTimeOffset?)document.AddedAt),
                LastTimeEntryAt = database.TimeEntries
                    .Where(entry => entry.MatterId == matter.Id)
                    .Max(entry => (DateTimeOffset?)entry.CreatedAt),
                LastInvoiceAt = database.Invoices
                    .Where(invoice => invoice.MatterId == matter.Id)
                    .Max(invoice => (DateTimeOffset?)invoice.CreatedAt),
                LastMovementAt = database.LedgerEntries
                    .Where(entry => entry.MatterId == matter.Id)
                    .Max(entry => (DateTimeOffset?)entry.CreatedAt),
            })
            .ToListAsync(cancellationToken);

        var recent = touched
            .Select(matter => new
            {
                matter.Id,
                matter.Reference,
                matter.Name,
                matter.ClientName,
                matter.Last,
                TouchedAt = MatterTouch.Latest(
                    matter.Last == null ? null : matter.Last.OccurredAt,
                    matter.LastDocumentAt,
                    matter.LastTimeEntryAt,
                    matter.LastInvoiceAt,
                    matter.LastMovementAt),
            })
            .Where(matter => matter.TouchedAt != null)
            .OrderByDescending(matter => matter.TouchedAt)
            .Take(RecentMatterCount)
            .ToList();

        var summary = new DashboardSummary(
            today,
            await database.Matters.CountAsync(matter => matter.ClosedOn == null, cancellationToken),
            await database.Contacts.CountAsync(cancellationToken),
            deadlines.Count(deadline => deadline.Date >= today &&
                                        deadline.Date <= today.AddDays(DeadlineUrgencyRule.ThisWeekDays)),
            [.. deadlines.Select(deadline => deadline with
            {
                Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
            })],
            nextBeyond,
            unbilled,
            // A dossier touched only by time entries has no journal line to summarise, and the row
            // says so rather than inventing one.
            [.. recent.Select(matter => new DashboardRecentMatter(
                matter.Id,
                matter.Reference,
                matter.Name,
                matter.ClientName,
                matter.Last?.Type,
                matter.Last?.Summary,
                matter.TouchedAt))],
            await HonorairesQuery.ForPracticeAsync(database, today, cancellationToken));

        return Results.Ok(summary);
    }

    /// <summary>
    /// « Temps saisi non facturé » — the only large number in the application, and the most forgotten
    /// thing in a solo practice. Per matter it is <c>billable time − ledger − invoiced</c>; matters
    /// already square, or in credit, are left out so the rows sum exactly to the headline.
    /// </summary>
    private static async Task<DashboardUnbilled> ComputeUnbilledAsync(
        AvocadoDbContext database,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var ageingCutoff = today.AddDays(-AgeingDays);

        // One pass over billable time for open matters. A solo practice has thousands of entries at
        // most, so this stays well inside what is reasonable to materialise.
        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.IsBillable && entry.Matter!.ClosedOn == null)
            .Select(entry => new
            {
                entry.MatterId,
                MatterName = entry.Matter!.Name,
                entry.DurationMinutes,
                entry.Date,
                RateCents = entry.HourlyRateCentsOverride ?? entry.Matter.HourlyRateCents,
            })
            .ToListAsync(cancellationToken);

        var ledgerByMatter = await database.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.Matter!.ClosedOn == null)
            .GroupBy(entry => entry.MatterId)
            .Select(group => new { MatterId = group.Key, Cents = group.Sum(entry => entry.AmountCents) })
            .ToDictionaryAsync(group => group.MatterId, group => group.Cents, cancellationToken);

        var invoicedByMatter = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Matter!.ClosedOn == null)
            .GroupBy(invoice => invoice.MatterId)
            .Select(group => new
            {
                MatterId = group.Key,
                Cents = group.Sum(invoice => invoice.AmountExclVatCents),
            })
            .ToDictionaryAsync(group => group.MatterId, group => group.Cents, cancellationToken);

        var perMatter = entries
            .GroupBy(entry => new { entry.MatterId, entry.MatterName })
            .Select(group =>
            {
                var billableCents = group.Sum(entry => entry.RateCents * entry.DurationMinutes / 60);
                var settled = ledgerByMatter.GetValueOrDefault(group.Key.MatterId)
                              + invoicedByMatter.GetValueOrDefault(group.Key.MatterId);

                return new
                {
                    group.Key.MatterId,
                    group.Key.MatterName,
                    Minutes = group.Sum(entry => entry.DurationMinutes),
                    LeftToBillCents = billableCents - settled,
                    AgedCents = group
                        .Where(entry => entry.Date < ageingCutoff)
                        .Sum(entry => entry.RateCents * entry.DurationMinutes / 60),
                };
            })
            .Where(matter => matter.LeftToBillCents > 0)
            .OrderByDescending(matter => matter.LeftToBillCents)
            .ToList();

        return new DashboardUnbilled(
            perMatter.Sum(matter => matter.LeftToBillCents),
            perMatter.Sum(matter => matter.Minutes),
            perMatter.Count,
            perMatter.Sum(matter => matter.AgedCents),
            [.. perMatter.Select(matter => new DashboardUnbilledMatter(
                matter.MatterId, matter.MatterName, matter.Minutes, matter.LeftToBillCents))]);
    }

    /// <summary>One client, deliberately — the first by creation order.</summary>
    private static string? ClientNameOf(Matters.Matter matter) => matter.Parties
        .Where(party => party.IsClient)
        .OrderBy(party => party.Id)
        .Select(party => party.Contact!.Type == ContactType.Organisation
            ? party.Contact!.LegalName
            : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
        .FirstOrDefault();
}
