using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <param name="TimeEntryIds">The lines this facture covers. They are marked billed and stop counting.</param>
/// <param name="AmountExclVatCents">
/// What she actually billed. Defaults to what the selected hours are worth; overriding it is the
/// whole point, and the difference is recorded as the boni or the mali.
/// </param>
public sealed record BillTimeInput(
    IReadOnlyList<Guid> TimeEntryIds,
    DateOnly Date,
    long? AmountExclVatCents,
    string? ExternalReference,
    bool IsPaid = false);

/// <summary>
/// Establishes a facture from selected temps passé.
/// <para>
/// Lawyers rarely bill everything at once, so this is the normal path: pick the lines, see what they
/// are worth, decide what to bill, and the difference between the two is the figure she actually
/// wants to watch. Billing above the recorded time is a <b>boni</b>, below it a <b>mali</b>, and both
/// are deliberate acts worth measuring across a practice.
/// </para>
/// </summary>
public static class BillTimeEntries
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        BillTimeInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.TimeEntryIds.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeEntries"] = ["Choisissez au moins une ligne de temps passé."],
            });
        }

        var matter = await database.Matters
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var entries = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && input.TimeEntryIds.Contains(entry.Id))
            .ToListAsync(cancellationToken);

        if (entries.Count != input.TimeEntryIds.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeEntries"] = ["Certaines lignes n'appartiennent pas à ce dossier."],
            });
        }

        if (entries.Any(entry => entry.InvoiceId is not null))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeEntries"] = ["Certaines lignes sont déjà rattachées à une facture."],
            });
        }

        var billedTimeCents = entries.Sum(entry => entry.AmountCents(matter.HourlyRateCents));
        var amount = input.AmountExclVatCents ?? billedTimeCents;

        if (amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["Le montant doit être positif."],
            });
        }

        var invoice = new BillingInvoice
        {
            MatterId = matterId,
            Date = input.Date,
            AmountExclVatCents = amount,
            BilledTimeCents = billedTimeCents,
            ExternalReference = string.IsNullOrWhiteSpace(input.ExternalReference)
                ? null
                : input.ExternalReference.Trim(),
            IsPaid = input.IsPaid,
            PaidOn = input.IsPaid ? input.Date : null,
        };

        database.Invoices.Add(invoice);

        foreach (var entry in entries)
        {
            entry.InvoiceId = invoice.Id;
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/invoices/{invoice.Id}",
            new { invoice.Id, invoice.AmountExclVatCents, invoice.BilledTimeCents, invoice.VarianceCents });
    }
}
