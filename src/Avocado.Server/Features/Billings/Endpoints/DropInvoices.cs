using Avocado.Server.Data;
using Avocado.Server.Features.Billings.Infrastructure;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Imports.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <param name="Paths">Absolute, as the shell resolved them from what she dropped.</param>
public sealed record ReadDroppedInput(IReadOnlyList<string> Paths);

/// <param name="Path">Echoed back so the window can match an answer to the file it dropped.</param>
/// <param name="SuggestedInvoiceId">
/// An existing facture this is almost certainly the PDF of. Null means « nothing here matches », and
/// the window offers to create a line instead.
/// </param>
/// <param name="WrongClient">
/// The facture names a client that is not this dossier's. Almost always a file dropped on the wrong
/// dossier, which is worth saying loudly before anything is written.
/// </param>
public sealed record DroppedInvoice(
    string Path,
    string FileName,
    ReadInvoice? Read,
    Guid? SuggestedInvoiceId,
    string? SuggestedReason,
    bool WrongClient,
    string? Error);

/// <param name="InvoiceId">Attach to this facture. Null creates one from what was read.</param>
/// <param name="TimeEntryIds">
/// The lines this facture settles, as she ticked them.
///
/// <para>The reason this parameter exists: a facture dropped on a dossier used to consume nothing, so
/// its hours went on sitting in « reste à facturer » and its boni/mali was zero. She would then solde
/// those hours en mali to clear them, and a mali appeared on a dossier that had never granted one.
/// Empty is still legitimate — a reprise settles nothing.</para>
/// </param>
public sealed record FileInvoiceInput(
    string Path,
    Guid? InvoiceId,
    DateOnly? Date,
    long? AmountExclVatCents,
    string? ExternalReference,
    bool IsPaid,
    bool IsHistorical,
    IReadOnlyList<Guid>? TimeEntryIds = null);

/// <summary>
/// Factures dropped onto a dossier, read and filed.
///
/// <para><b>Two things she does with the same gesture.</b> A facture she has just issued is dropped
/// onto the line Avocado already knows about, and becomes that line's document. A facture from before
/// Avocado existed has no line, and becomes one. Both start by dropping the PDF on the dossier, which
/// is why they are one endpoint pair rather than two features.</para>
///
/// <para><b>Reading is separate from writing, deliberately.</b> Nothing is recorded until she has
/// seen what was read and said yes. The figures come off a PDF by pattern matching, and a facture is
/// money: the one thing that must not happen is a silent import of a wrong amount, which she would
/// discover at the end of the year.</para>
///
/// <para><b>The dossier is known, so the client is a check rather than a guess.</b> She dropped the
/// file on a dossier's own Facturation tab, so nothing has to be inferred about where it belongs. The
/// SIREN on the invoice is then used the other way round: to say « cette facture est au nom de
/// WINE SERVICES » when the dossier's client is somebody else.</para>
/// </summary>
public static class DropInvoices
{
    /// <summary>Beside « Pièces », for the same reason: it is where she would have put them.</summary>
    public const string Folder = "Factures";

    public static async Task<IResult> ReadAsync(
        Guid matterId,
        ReadDroppedInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var clientSiren = Digits(matter.Parties
            .FirstOrDefault(party => party.IsClient)?.Contact is { Type: ContactType.Organisation } client
                ? client.Siren
                : null);

        var existing = await database.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.MatterId == matterId)
            .ToListAsync(cancellationToken);

        var answers = new List<DroppedInvoice>();

        foreach (var path in input.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            answers.Add(One(path, existing, clientSiren));
        }

        return Results.Ok(answers);
    }

    private static DroppedInvoice One(string path, List<BillingInvoice> existing, string? clientSiren)
    {
        var name = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            return new DroppedInvoice(path, name, null, null, null, false, "Ce fichier est introuvable.");
        }

        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new DroppedInvoice(path, name, null, null, null, false,
                "Seuls les PDF peuvent être lus. Déposez la facture telle que votre outil l'a produite.");
        }

        ReadInvoice read;

        try
        {
            using var stream = File.OpenRead(path);
            read = InvoiceReader.Read(stream);
        }
        catch (Exception exception)
        {
            // Every exception, deliberately. A file renamed to .pdf, a download that stopped
            // half-way, a PDF the library will not parse: each throws something different, and she
            // dropped fifteen files at once. One unreadable file reports itself and the other
            // fourteen are still read.
            return new DroppedInvoice(path, name, null, null, null, false,
                $"Ce PDF n'a pas pu être lu : {exception.Message}");
        }

        var (suggested, why) = Suggest(read, existing);

        var wrongClient = clientSiren is { Length: 9 }
            && read.ClientSiren is { Length: 9 }
            && clientSiren != read.ClientSiren;

        return new DroppedInvoice(path, name, read, suggested?.Id, why, wrongClient, null);
    }

    /// <summary>
    /// Which existing facture this PDF is of, and why.
    ///
    /// <para>The reference first, because her invoicing tool's number is the same thing she typed
    /// into Avocado, and an exact match is not a coincidence. Failing that, same amount on the same
    /// day: two factures for the identical sum on the identical date in one dossier is rare enough
    /// that offering it is right, and she confirms it either way.</para>
    ///
    /// <para>A facture that already has a document is never suggested: attaching a second PDF to it
    /// would replace one she has already filed.</para>
    /// </summary>
    private static (BillingInvoice? Invoice, string? Reason) Suggest(
        ReadInvoice read,
        List<BillingInvoice> existing)
    {
        var free = existing.Where(invoice => invoice.DocumentPath is null).ToList();

        if (read.Number is { Length: > 0 } number)
        {
            var byReference = free.FirstOrDefault(invoice =>
                string.Equals(invoice.ExternalReference?.Trim(), number, StringComparison.OrdinalIgnoreCase));

            if (byReference is not null)
            {
                return (byReference, $"Même numéro de facture ({number}).");
            }
        }

        if (read.TotalExclVatCents is { } amount && read.IssuedOn is { } issued)
        {
            var byFigures = free.FirstOrDefault(invoice =>
                invoice.AmountExclVatCents == amount && invoice.Date == issued);

            if (byFigures is not null)
            {
                return (byFigures, "Même montant, à la même date.");
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Copies the PDF into the dossier and either attaches it to a facture or creates one.
    ///
    /// <para>Copied, not moved. She dropped it from her Downloads or from her invoicing tool's own
    /// folder, and Avocado does not get to empty those.</para>
    /// </summary>
    public static async Task<IResult> FileAsync(
        Guid matterId,
        FileInvoiceInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        if (matter.DocumentsFolder is not { Length: > 0 } root || !Directory.Exists(root))
        {
            return Problem("Ce dossier n'a pas encore de dossier de documents : indiquez-le d'abord.");
        }

        if (!File.Exists(input.Path))
        {
            return Problem("Ce fichier n'est plus là.");
        }

        // Before anything is written, and before the file is copied: a selection that is refused must
        // not leave a PDF filed against a facture that was never completed.
        var (lines, lineFailure) = await InvoiceLines.ReadAsync(
            database, matterId, input.TimeEntryIds ?? [], cancellationToken);

        if (lineFailure is not null)
        {
            return Problem(lineFailure);
        }

        var invoice = input.InvoiceId is { } id
            ? await database.Invoices.FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.MatterId == matterId, cancellationToken)
            : null;

        if (input.InvoiceId is not null && invoice is null)
        {
            return Problem("Cette facture n'existe plus.");
        }

        if (invoice is null)
        {
            if (input.AmountExclVatCents is not { } amount || amount <= 0 || input.Date is not { } date)
            {
                return Problem("Indiquez au moins la date et le montant HT de cette facture.");
            }

            invoice = new BillingInvoice
            {
                MatterId = matterId,
                Date = date,
                AmountExclVatCents = amount,
                ExternalReference = Trimmed(input.ExternalReference),
                IsPaid = input.IsPaid,
                PaidOn = input.IsPaid ? date : null,
                // Backfilled factures are historical by default: their hours were never recorded
                // here, so subtracting them from « reste à facturer » would open the dossier at a
                // large negative figure. The window sends false when she is filing today's facture.
                IsHistorical = input.IsHistorical,
            };

            database.Invoices.Add(invoice);
        }

        // The lines she ticked, taken by this facture exactly as « facturer du temps passé » takes
        // them: they stop counting as unbilled, and what they were worth becomes this facture's
        // boni or mali. See InvoiceLines.
        InvoiceLines.Attach(lines, invoice, matter.HourlyRateCents);

        string relative;

        try
        {
            relative = Copy(root, input.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Problem($"La facture n'a pas pu être copiée : {exception.Message}");
        }

        invoice.DocumentPath = relative;

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            invoice.Id,
            relativePath = relative,
            invoice.BilledTimeCents,
            invoice.VarianceCents,
            settledLines = lines.Count,
        });
    }

    private static string Copy(string root, string source)
    {
        var folder = Path.Combine(root, Folder);
        Directory.CreateDirectory(folder);

        var name = PortableFileName.Clean(Path.GetFileName(source), '-');
        var destination = Free(Path.Combine(folder, name));

        File.Copy(source, destination, overwrite: false);

        return $"{Folder}/{Path.GetFileName(destination)}";
    }

    /// <summary>« FACTURE_92.pdf », then « FACTURE_92 (2).pdf ». Nothing already filed is replaced.</summary>
    private static string Free(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({index}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static string? Digits(string? value) =>
        value is null ? null : new string([.. value.Where(char.IsAsciiDigit)]);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Facture", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
