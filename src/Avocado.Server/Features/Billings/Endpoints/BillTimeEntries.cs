using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <param name="TimeEntryIds">The lines this facture covers. They are marked billed and stop counting.</param>
/// <param name="AmountExclVatCents">
/// What she actually billed. Defaults to what the selected hours are worth; overriding it is the
/// whole point, and the difference is recorded as the boni or the mali.
/// </param>
/// <param name="IsAbandon">
/// « Je ne facturerai pas ces heures. » The same act as billing, minus the facture: the lines are
/// consumed so they stop waiting, and the whole of what they were worth becomes a mali.
///
/// <para>Here rather than in an endpoint of its own because the hard part is identical, taking the
/// right lines out of the unbilled total, and a second copy of that would be the third place « reste
/// à facturer » is computed. The amount is the only thing that branches.</para>
/// </param>
public sealed record BillTimeInput(
    IReadOnlyList<Guid> TimeEntryIds,
    DateOnly Date,
    long? AmountExclVatCents,
    string? ExternalReference,
    bool IsPaid = false,
    bool IsAbandon = false);

/// <summary>
/// Establishes a facture from selected temps passé, or writes those hours off.
/// <para>
/// Lawyers rarely bill everything at once, so this is the normal path: pick the lines, see what they
/// are worth, decide what to bill, and the difference between the two is the figure she actually
/// wants to watch. Billing above the recorded time is a <b>boni</b>, below it a <b>mali</b>, and both
/// are deliberate acts worth measuring across a practice.
/// </para>
/// <para>
/// An abandon is the same act with nothing billed. « Sur ce dossier j'ai 1 500 en temps passé, j'ai
/// facturé 1 000 et ne pourrai pas facturer plus »: without it those hours wait for ever and the
/// dossier never settles. It is not turnover and never shows as facturé; it is a mali of everything
/// it consumed, and deleting it hands the lines back through the same foreign key.
/// </para>
/// <para>
/// Taking the lines is <see cref="InvoiceLines"/>'s job, shared with the two other ways a facture
/// comes into existence. It used to live here alone, which is why those two did not do it.
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
                [InvoiceLines.Field] = ["Choisissez au moins une ligne de temps passé."],
            });
        }

        var matter = await database.Matters
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var (lines, failure) = await InvoiceLines.ReadAsync(
            database, matterId, input.TimeEntryIds, cancellationToken);

        if (failure is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [InvoiceLines.Field] = [failure],
            });
        }

        var billedTimeCents = InvoiceLines.Worth(lines, matter.HourlyRateCents);

        // An abandon bills nothing by definition, so an amount sent with one is ignored rather than
        // rejected: the client has no field to type it in and would have no way to correct itself.
        var amount = input.IsAbandon ? 0 : input.AmountExclVatCents ?? billedTimeCents;

        if (!input.IsAbandon && amount <= 0)
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
            IsAbandon = input.IsAbandon,
            ExternalReference = string.IsNullOrWhiteSpace(input.ExternalReference)
                ? null
                : input.ExternalReference.Trim(),
            // Nothing was billed, so nothing can have been paid.
            IsPaid = !input.IsAbandon && input.IsPaid,
            PaidOn = !input.IsAbandon && input.IsPaid ? input.Date : null,
        };

        database.Invoices.Add(invoice);

        InvoiceLines.Attach(lines, invoice, matter.HourlyRateCents);

        await database.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/invoices/{invoice.Id}",
            new { invoice.Id, invoice.AmountExclVatCents, invoice.BilledTimeCents, invoice.VarianceCents });
    }
}
