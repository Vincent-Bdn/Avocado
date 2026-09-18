using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Matters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// Factures dropped onto a dossier: read, matched to a line or turned into one, and filed.
///
/// <para>The figures come off a PDF by pattern matching and they are money, so nothing here is
/// written without her having seen it. What these tests hold is that the two halves stay separate:
/// reading proposes, filing records, and the proposal is never acted on by itself.</para>
/// </summary>
public sealed class DropInvoicesTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avocado-factures-" + Guid.NewGuid().ToString("N"));

    public DropInvoicesTests() => Directory.CreateDirectory(_root);

    private Matter Dossier(string? clientSiren = null)
    {
        var matter = _vault.Save(new Matter
        {
            Reference = "2026-0001",
            Name = "Litige PHOENIX",
            OpenedOn = new DateOnly(2026, 1, 5),
            DocumentsFolder = _root,
        });

        if (clientSiren is not null)
        {
            var contact = _vault.Save(new Contact
            {
                Type = ContactType.Organisation,
                LegalName = "N'JOY MOMENTS",
                Siren = clientSiren,
            });

            _vault.Save(new MatterParty { MatterId = matter.Id, ContactId = contact.Id, IsClient = true });
        }

        return matter;
    }

    private BillingInvoice Facture(Matter matter, string? reference, long cents, DateOnly date) =>
        _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = date,
            AmountExclVatCents = cents,
            ExternalReference = reference,
        });

    /// <summary>A file on disk that is not a readable PDF, for the paths that never reach the reader.</summary>
    private string Dropped(string name, string contents = "not a pdf")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);

        return path;
    }

    private async Task<List<DroppedInvoice>> Read(Matter matter, params string[] paths)
    {
        var result = await DropInvoices.ReadAsync(
            matter.Id, new ReadDroppedInput(paths), _vault.Database, default);

        return Assert.IsType<List<DroppedInvoice>>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    private Task<IResult> File_(Matter matter, FileInvoiceInput input) =>
        DropInvoices.FileAsync(matter.Id, input, _vault.Database, default);

    /// <summary>A readable invoice, laid out as the cabinet's own tool lays one out.</summary>
    private string RealPdf(string name, string number = "92", string ht = "600,00",
        string issued = "14/09/2026", string clientSiren = "828 768 028")
    {
        var path = Path.Combine(_root, name);
        System.IO.File.WriteAllBytes(path, Imports.SyntheticInvoice.Build(
            number: number, ht: ht, issued: issued, clientSiren: clientSiren,
            tva: "120,00", ttc: "720,00"));

        return path;
    }

    [Fact]
    public async Task ReadsADroppedInvoiceAndProposesNothingWhenNoLineMatches()
    {
        var matter = Dossier();
        var answers = await Read(matter, RealPdf("FACTURE_92.pdf"));

        Assert.Null(answers[0].Error);
        Assert.NotNull(answers[0].Read);
        Assert.Equal("92", answers[0].Read!.Number);
        Assert.Equal(60_000, answers[0].Read!.TotalExclVatCents);

        // Nothing recorded to match against, so the window offers to create a line.
        Assert.Null(answers[0].SuggestedInvoiceId);
    }

    [Fact]
    public async Task ProposesTheFactureWithTheSameNumber()
    {
        var matter = Dossier();
        var existing = Facture(matter, "92", 99_999, new DateOnly(2020, 1, 1));

        var answers = await Read(matter, RealPdf("FACTURE_92.pdf"));

        // The amount and the date disagree, and the reference still wins: her invoicing tool's
        // number is the same thing she typed into Avocado, and an exact match is not a coincidence.
        Assert.Equal(existing.Id, answers[0].SuggestedInvoiceId);
        Assert.Contains("numéro", answers[0].SuggestedReason);
    }

    [Fact]
    public async Task ProposesTheFactureWithTheSameFiguresWhenThereIsNoNumber()
    {
        var matter = Dossier();
        var existing = Facture(matter, null, 60_000, new DateOnly(2026, 9, 14));

        var answers = await Read(matter, RealPdf("FACTURE_92.pdf"));

        Assert.Equal(existing.Id, answers[0].SuggestedInvoiceId);
        Assert.Contains("montant", answers[0].SuggestedReason);
    }

    [Fact]
    public async Task NeverProposesAFactureThatAlreadyHasItsPdf()
    {
        var matter = Dossier();
        var taken = Facture(matter, "92", 60_000, new DateOnly(2026, 9, 14));
        taken.DocumentPath = "Factures/déjà-là.pdf";
        _vault.Database.SaveChanges();

        var answers = await Read(matter, RealPdf("FACTURE_92.pdf"));

        // Attaching a second PDF would replace one she has already filed.
        Assert.Null(answers[0].SuggestedInvoiceId);
    }

    /// <summary>
    /// Two SIRENs are on every invoice, and the dossier knows whose it is. Dropping WINE SERVICES'
    /// facture on the N'JOY MOMENTS dossier is a mistake worth saying out loud before anything is
    /// written.
    /// </summary>
    [Fact]
    public async Task SaysSoWhenTheFactureNamesAnotherClient()
    {
        var matter = Dossier(clientSiren: "828 768 028");

        var mine = await Read(matter, RealPdf("bonne.pdf", clientSiren: "828 768 028"));
        Assert.False(mine[0].WrongClient);

        var theirs = await Read(matter, RealPdf("mauvaise.pdf", clientSiren: "520 132 069"));
        Assert.True(theirs[0].WrongClient);
    }

    [Fact]
    public async Task SaysNothingAboutTheClientWhenTheDossierHasNoSiren()
    {
        // A personne physique has no SIREN, and « this facture might be someone else's » is not a
        // thing to say on no evidence.
        var matter = Dossier();
        var answers = await Read(matter, RealPdf("FACTURE_92.pdf", clientSiren: "520 132 069"));

        Assert.False(answers[0].WrongClient);
    }

    [Fact]
    public async Task RefusesAnythingThatIsNotAPdf()
    {
        var matter = Dossier();
        var answers = await Read(matter, Dropped("facture.docx"));

        Assert.NotNull(answers[0].Error);
        Assert.Contains("PDF", answers[0].Error);
        Assert.Null(answers[0].Read);
    }

    [Fact]
    public async Task SaysSoWhenTheFileHasGone()
    {
        var matter = Dossier();
        var answers = await Read(matter, Path.Combine(_root, "jamais-existé.pdf"));

        Assert.Contains("introuvable", answers[0].Error);
    }

    /// <summary>
    /// The backfill case: no line matches, so the window offers to create one.
    /// </summary>
    [Fact]
    public async Task CreatesAFactureFromWhatWasDropped()
    {
        var matter = Dossier();
        var path = Dropped("FACTURE_92.pdf");

        var result = await File_(matter, new FileInvoiceInput(
            path, null, new DateOnly(2026, 9, 14), 60_000, "92", IsPaid: true, IsHistorical: true));

        Assert.IsAssignableFrom<IValueHttpResult>(result);

        var invoice = await _vault.Database.Invoices.SingleAsync();

        Assert.Equal(60_000, invoice.AmountExclVatCents);
        Assert.Equal(new DateOnly(2026, 9, 14), invoice.Date);
        Assert.Equal("92", invoice.ExternalReference);
        Assert.True(invoice.IsPaid);
        Assert.Equal(new DateOnly(2026, 9, 14), invoice.PaidOn);
        Assert.Equal("Factures/FACTURE_92.pdf", invoice.DocumentPath);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Factures", "FACTURE_92.pdf")));
    }

    /// <summary>
    /// A facture backfilled from before Avocado has no hours recorded against it, so subtracting it
    /// from « reste à facturer » would open the dossier at a large negative figure. That was a real
    /// bug once: a dossier repris de Gestisoft showed − 8 794 € and was dropped from the list for
    /// being negative.
    /// </summary>
    [Fact]
    public async Task MarksABackfilledFactureHistorical()
    {
        var matter = Dossier();

        await File_(matter, new FileInvoiceInput(
            Dropped("ancienne.pdf"), null, new DateOnly(2024, 3, 1), 500_000, "12",
            IsPaid: true, IsHistorical: true));

        Assert.True((await _vault.Database.Invoices.SingleAsync()).IsHistorical);
    }

    [Fact]
    public async Task AttachesToTheFactureSheChose()
    {
        var matter = Dossier();
        var existing = Facture(matter, "92", 60_000, new DateOnly(2026, 9, 14));

        await File_(matter, new FileInvoiceInput(
            Dropped("FACTURE_92.pdf"), existing.Id, null, null, null, IsPaid: false, IsHistorical: false));

        _vault.Database.ChangeTracker.Clear();
        var after = await _vault.Database.Invoices.SingleAsync();

        Assert.Equal("Factures/FACTURE_92.pdf", after.DocumentPath);
        // Attaching a document changes nothing about the money.
        Assert.Equal(60_000, after.AmountExclVatCents);
        Assert.Equal("92", after.ExternalReference);
        Assert.Single(await _vault.Database.Invoices.ToListAsync());
    }

    [Fact]
    public async Task CopiesRatherThanMovesWhatSheDropped()
    {
        var matter = Dossier();
        var path = Dropped("FACTURE_92.pdf");

        await File_(matter, new FileInvoiceInput(
            path, null, new DateOnly(2026, 9, 14), 60_000, "92", IsPaid: false, IsHistorical: false));

        // She dropped it from her Downloads or from her invoicing tool's own folder, and Avocado
        // does not get to empty those.
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public async Task NeverWritesOverAFactureAlreadyFiled()
    {
        var matter = Dossier();

        await File_(matter, new FileInvoiceInput(
            Dropped("FACTURE_92.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "92", false, false));

        // The same file name again, a different facture. The first one is not replaced.
        await File_(matter, new FileInvoiceInput(
            Dropped("FACTURE_92.pdf"), null, new DateOnly(2026, 10, 14), 70_000, "93", false, false));

        var filed = Directory.GetFiles(Path.Combine(_root, "Factures")).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["FACTURE_92 (2).pdf", "FACTURE_92.pdf"], filed);
    }

    [Fact]
    public async Task RefusesADossierWithNoFolder()
    {
        var matter = _vault.Save(new Matter
        {
            Reference = "2026-0002",
            Name = "Sans répertoire",
            OpenedOn = new DateOnly(2026, 1, 5),
        });

        var result = await File_(matter, new FileInvoiceInput(
            Dropped("x.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "92", false, false));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Empty(await _vault.Database.Invoices.ToListAsync());
    }

    [Fact]
    public async Task RefusesToCreateAFactureWithNoAmount()
    {
        var matter = Dossier();

        var result = await File_(matter, new FileInvoiceInput(
            Dropped("x.pdf"), null, new DateOnly(2026, 9, 14), null, "92", false, false));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Empty(await _vault.Database.Invoices.ToListAsync());

        // And nothing was copied either: a refused facture leaves no file behind.
        Assert.False(Directory.Exists(Path.Combine(_root, "Factures")));
    }

    [Fact]
    public async Task ReadingRecordsNothing()
    {
        var matter = Dossier();
        var answers = await Read(matter, Dropped("FACTURE_92.pdf"));

        // Not a real PDF, so it reports itself rather than throwing and losing the batch.
        Assert.NotNull(answers[0].Error);

        // The whole reason reading and filing are two endpoints.
        Assert.Empty(await _vault.Database.Invoices.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(_root, "Factures")));
    }

    public void Dispose()
    {
        _vault.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
