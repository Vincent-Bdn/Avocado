using Avocado.Server.Data;
using Avocado.Server.Features.TimeEntries.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.TimeEntries.Endpoints;

public static class ListTimeEntries
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .Where(candidate => candidate.Id == matterId)
            .Select(candidate => new { candidate.HourlyRateCents })
            .FirstOrDefaultAsync(cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.MatterId == matterId)
            .OrderByDescending(entry => entry.Date)
            .ThenByDescending(entry => entry.StartedAt)
            .Select(entry => new
            {
                entry.Id,
                entry.Date,
                entry.StartedAt,
                entry.Task,
                entry.DurationMinutes,
                entry.IsBillable,
                entry.HourlyRateCentsOverride,
                entry.ActivityId,
                entry.InvoiceId,
                InvoiceReference = entry.Invoice!.ExternalReference,
            })
            .ToListAsync(cancellationToken);

        var items = entries
            .Select(entry =>
            {
                var rate = entry.HourlyRateCentsOverride ?? matter.HourlyRateCents;
                return new TimeEntryListItem(
                    entry.Id,
                    entry.Date,
                    entry.StartedAt,
                    entry.Task,
                    entry.DurationMinutes,
                    entry.IsBillable,
                    rate,
                    entry.HourlyRateCentsOverride is not null,
                    entry.IsBillable ? rate * entry.DurationMinutes / 60 : 0,
                    entry.ActivityId,
                    entry.InvoiceId,
                    entry.InvoiceReference);
            })
            .ToList();

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Monday-based, matching French convention; « cette semaine » must not reset on Sunday.
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var totals = new TimeEntryTotals(
            items.Where(item => item.Date == today).Sum(item => item.DurationMinutes),
            items.Where(item => item.Date >= weekStart).Sum(item => item.DurationMinutes),
            items.Sum(item => item.DurationMinutes),
            items.Where(item => item.IsBillable).Sum(item => item.DurationMinutes),
            items.Where(item => !item.IsBillable).Sum(item => item.DurationMinutes),
            items.Sum(item => item.AmountCents));

        return Results.Ok(new TimeEntryListPage(items, totals));
    }
}
