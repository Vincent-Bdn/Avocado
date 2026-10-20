using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Dashboards;

/// <summary>One dossier's share of a period's honoraires.</summary>
/// <param name="UnpaidCents">
/// What is billed and not in. The column she is actually looking for, and the list is ordered by it:
/// « parmi ces 10 985 €, non payé 1 200 € : à quels dossiers se rapportent ces 1 200 € ? Permet de
/// savoir qui relancer. »
/// </param>
/// <param name="BillableCents">
/// Time recorded in the period on this dossier, at its rate. Not part of the facturé question, and
/// here because the same click answers the other half of the chart: a dossier with hours and no
/// facture in the month is what the écart is made of.
/// </param>
/// <param name="VarianceCents">Boni or mali decided on this dossier's factures in the period.</param>
public sealed record HonoraireMatter(
    Guid MatterId,
    string Reference,
    string Name,
    string? ClientName,
    long InvoicedCents,
    long PaidCents,
    long UnpaidCents,
    long BillableCents,
    long VarianceCents,
    int InvoiceCount);

/// <summary>
/// Which dossiers a column of the chart is made of.
///
/// <para>The chart answered « septembre : facturé 10 985 €, dont 1 200 € non payés » and stopped
/// there, which is one question short of being useful: the figure that matters is a list of names to
/// telephone. Every row behind the aggregate already carried its <c>MatterId</c>; nothing new had to
/// be recorded, the grouping simply was not offered.</para>
///
/// <para>Ordered by unpaid first, because that is the question. A dossier with nothing outstanding
/// is still listed — it is part of what was billed — but it sorts below everything that is waiting.</para>
///
/// <para>Abandons are absent, like everywhere else money coming in is counted. Their mali shows on
/// the dossier itself.</para>
/// </summary>
public static class HonorairesDetailQuery
{
    public static async Task<IReadOnlyList<HonoraireMatter>> ForPeriodAsync(
        AvocadoDbContext database,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var end = to.AddDays(1);

        var invoices = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Date >= from && invoice.Date < end && !invoice.IsAbandon)
            .Select(invoice => new
            {
                invoice.MatterId,
                invoice.AmountExclVatCents,
                invoice.BilledTimeCents,
                invoice.IsPaid,
                invoice.IsHistorical,
            })
            .ToListAsync(cancellationToken);

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.IsBillable && entry.Date >= from && entry.Date < end)
            .Select(entry => new
            {
                entry.MatterId,
                entry.DurationMinutes,
                Rate = entry.HourlyRateCentsOverride ?? entry.Matter!.HourlyRateCents,
            })
            .ToListAsync(cancellationToken);

        var touched = invoices
            .Select(invoice => invoice.MatterId)
            .Concat(entries.Select(entry => entry.MatterId))
            .Distinct()
            .ToList();

        if (touched.Count == 0)
        {
            return [];
        }

        // Named in one pass. Closed dossiers included: a facture issued in mars on a dossier closed
        // in juin is still that month's turnover, and leaving it out would make the rows disagree
        // with the column they were opened from.
        var named = await database.Matters
            .AsNoTracking()
            .Where(matter => touched.Contains(matter.Id))
            .Select(matter => new
            {
                matter.Id,
                matter.Reference,
                matter.Name,
                ClientName = ClientNameOf(matter),
            })
            .ToListAsync(cancellationToken);

        var billableByMatter = entries
            .GroupBy(entry => entry.MatterId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(entry => entry.Rate * entry.DurationMinutes / 60));

        var rows = named.Select(matter =>
        {
            var mine = invoices.Where(invoice => invoice.MatterId == matter.Id).ToList();

            var invoiced = mine.Sum(invoice => invoice.AmountExclVatCents);

            // Paid is the settled part of what is not a reprise, exactly as the chart's own segments
            // split it. Otherwise the rows would not add up to the column they came from.
            var paid = mine
                .Where(invoice => invoice.IsPaid && !invoice.IsHistorical)
                .Sum(invoice => invoice.AmountExclVatCents);

            var historical = mine
                .Where(invoice => invoice.IsHistorical)
                .Sum(invoice => invoice.AmountExclVatCents);

            return new HonoraireMatter(
                matter.Id,
                matter.Reference,
                matter.Name,
                matter.ClientName,
                invoiced,
                paid,
                invoiced - historical - paid,
                billableByMatter.GetValueOrDefault(matter.Id),
                mine.Where(invoice => invoice.BilledTimeCents != 0)
                    .Sum(invoice => invoice.AmountExclVatCents - invoice.BilledTimeCents),
                mine.Count);
        });

        return
        [
            .. rows
                .OrderByDescending(matter => matter.UnpaidCents)
                .ThenByDescending(matter => matter.InvoicedCents)
                .ThenBy(matter => matter.Name),
        ];
    }

    /// <summary>One client, deliberately, the first by creation order. As the accueil does it.</summary>
    private static string? ClientNameOf(Matter matter) => matter.Parties
        .Where(party => party.IsClient)
        .OrderBy(party => party.Id)
        .Select(party => party.Contact!.Type == ContactType.Organisation
            ? party.Contact!.LegalName
            : (party.Contact!.FirstName + " " + party.Contact!.LastName).Trim())
        .FirstOrDefault();
}
