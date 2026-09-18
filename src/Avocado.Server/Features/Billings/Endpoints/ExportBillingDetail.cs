using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Infrastructure;
using Avocado.Server.Features.Templates;
using Avocado.Server.Features.Templates.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <summary>
/// « Détail facture »: the annexe that goes to the client with the facture.
///
/// <para>A client who receives « honoraires : 6 000 € » and nothing else asks what it covers, and the
/// answer arrives three weeks later by email. Sending the detail with the invoice is what stops that
/// conversation happening at all.</para>
///
/// <para><b>It was a spreadsheet and is now a document.</b> The reasoning for sending it was right;
/// the artefact was wrong. This page is read by a client, sometimes by a bâtonnier, occasionally by a
/// juge taxateur, and a .xlsx is not something one sends to any of them.</para>
///
/// <para><b>Her document if she has given one, Avocado's otherwise.</b> A modèle whose kind is
/// « Détail de facture » wins: it carries her letterhead, her columns and her closing sentence, and
/// the row marked <c>{{ligne.…}}</c> is repeated per diligence. Without one, the built-in layout
/// below still produces something sendable on the first day, which is what stops this being a
/// feature that needs setting up before it does anything. See <see cref="BillingDetailTemplate"/>.</para>
/// </summary>
public static class ExportBillingDetail
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        var invoice = await database.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (invoice is null)
        {
            return Results.NotFound();
        }

        var matter = await database.Matters
            .AsNoTracking()
            .FirstAsync(candidate => candidate.Id == invoice.MatterId, cancellationToken);

        var entries = await database.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.InvoiceId == id)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartedAt)
            .Select(entry => new
            {
                entry.Date,
                entry.DurationMinutes,
                entry.Task,
                entry.HourlyRateCentsOverride,
            })
            .ToListAsync(cancellationToken);

        var lines = entries
            .Select(entry => new DetailLine(
                entry.Date,
                entry.DurationMinutes,
                entry.Task,
                entry.HourlyRateCentsOverride ?? matter.HourlyRateCents))
            .ToList();

        var bytes = await FromHerTemplateAsync(database, vaults, matter, invoice, lines, cancellationToken)
            ?? BillingDetailDocument.Build(
                invoice.ExternalReference,
                invoice.Date,
                matter.Reference,
                matter.Name,
                lines,
                invoice.DiscountBasisPoints,
                invoice.DiscountReason,
                invoice.AmountExclVatCents);

        var name = $"detail-facture-{matter.Reference}-{invoice.Date:yyyyMMdd}.docx";

        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            name);
    }

    /// <summary>
    /// Fills the cabinet's own modèle, or null when it has not given one, or when filling it fails.
    ///
    /// <para>A template that will not open must not lose her the détail: she presses the button
    /// minutes before sending a facture, and the built-in document is a correct answer. The failure
    /// is worth knowing about, which is what the Modèles screen is for, not this button.</para>
    /// </summary>
    private static async Task<byte[]?> FromHerTemplateAsync(
        AvocadoDbContext database,
        IVaultStore vaults,
        Matters.Matter matter,
        BillingInvoice invoice,
        IReadOnlyList<DetailLine> lines,
        CancellationToken cancellationToken)
    {
        var template = await database.Templates
            .AsNoTracking()
            .Where(candidate => candidate.Kind == BillingDetailTemplate.Kind)
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (template is null)
        {
            return null;
        }

        try
        {
            await using var content = vaults.Get(Guid.Empty).Blobs
                .OpenRead(new BlobReference(template.BlobSha256, template.SizeBytes));

            return TemplateMerge.Fill(
                content,
                BillingDetailTemplate.Values(
                    invoice.ExternalReference,
                    invoice.Date,
                    matter.Reference,
                    matter.Name,
                    matter.CourtCaseNumber,
                    lines,
                    invoice.DiscountBasisPoints,
                    invoice.DiscountReason,
                    invoice.AmountExclVatCents),
                BillingDetailTemplate.Rows(lines),
                BillingDetailTemplate.RowPrefix);
        }
        catch (Exception exception) when (exception is VaultException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
