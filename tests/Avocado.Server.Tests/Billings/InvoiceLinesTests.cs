using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Billings.Endpoints.Dtos;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// Every way a facture comes into existence takes the lines it settles.
///
/// <para>Three ways: from temps passé, by dropping its PDF on the dossier, and typed in by hand. Only
/// the first one did it, so the other two produced a facture that consumed nothing: the hours went on
/// sitting in « reste à facturer », and the boni/mali was zero because there was nothing to compare
/// the amount against.</para>
///
/// <para>What that looked like on the dossier PIRBAY: the facture is dropped, nothing is offered to
/// settle, the hours are still waiting, so they are soldées en mali — and a mali appears on a dossier
/// that never granted one. These tests hold the three paths to the same rule.</para>
/// </summary>
public sealed class InvoiceLinesTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "avocado-lignes-" + Guid.NewGuid().ToString("N"));

    public InvoiceLinesTests() => Directory.CreateDirectory(_root);

    /// <summary>200 €/h, so an hour is worth 20 000 centimes and the arithmetic reads at a glance.</summary>
    private Matter Dossier(long? forfaitCents = null) => _vault.Save(new Matter
    {
        Reference = "2026-0007",
        Name = "PIRBAY",
        OpenedOn = new DateOnly(2026, 1, 5),
        HourlyRateCents = 20_000,
        FixedFeeCents = forfaitCents,
        DocumentsFolder = _root,
    });

    private TimeEntry Heures(Matter matter, int minutes, bool billable = true) =>
        _vault.Save(new TimeEntry
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 3, 10),
            DurationMinutes = minutes,
            Task = "Rédaction",
            IsBillable = billable,
        });

    private string Pdf(string name) =>
        Path.Combine(_root, name) is var path && Write(path) ? path : path;

    private static bool Write(string path)
    {
        File.WriteAllBytes(path, Imports.SyntheticInvoice.Build(
            number: "92", ht: "600,00", issued: "14/09/2026",
            clientSiren: "828 768 028", tva: "120,00", ttc: "720,00"));

        return true;
    }

    private async Task<BillingInvoice> Reload(Guid id) =>
        await _vault.Database.Invoices.AsNoTracking().FirstAsync(invoice => invoice.Id == id);

    // ─── Dropping the PDF, which is the PIRBAY gesture ────────────────────────────────────────────

    [Fact]
    public async Task ADroppedFactureTakesTheLinesSheTicked()
    {
        var matter = Dossier();
        var twoHours = Heures(matter, 120);
        var oneHour = Heures(matter, 60);

        var result = await DropInvoices.FileAsync(
            matter.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_92.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "92", false, false,
                [twoHours.Id, oneHour.Id]),
            _vault.Database,
            default);

        Assert.IsAssignableFrom<IValueHttpResult>(result);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        // Three hours at 200 € is 600 € of time, billed 600 €: no arbitrage, and the lines are gone
        // from what is still to bill.
        Assert.Equal(60_000, invoice.BilledTimeCents);
        Assert.Equal(0, invoice.VarianceCents);
        Assert.Empty(await _vault.Database.TimeEntries
            .Where(entry => entry.InvoiceId == null)
            .ToListAsync());
    }

    [Fact]
    public async Task ADroppedFactureBelowTheValueOfItsLinesIsAMali()
    {
        var matter = Dossier();
        var fourHours = Heures(matter, 240);

        await DropInvoices.FileAsync(
            matter.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_93.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "93", false, false,
                [fourHours.Id]),
            _vault.Database,
            default);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        // 800 € of hours billed 600 €: a mali of 200 €, granted on purpose, and recorded as one
        // rather than discovered later by soldering the hours en mali by hand.
        Assert.Equal(80_000, invoice.BilledTimeCents);
        Assert.Equal(-20_000, invoice.VarianceCents);
    }

    [Fact]
    public async Task ADroppedFactureSettlingNothingStillFiles()
    {
        var matter = Dossier();
        Heures(matter, 120);

        await DropInvoices.FileAsync(
            matter.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_94.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "94", false, false),
            _vault.Database,
            default);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        // A reprise settles nothing, and that is what BilledTimeCents of zero means. The hours stay.
        Assert.Equal(0, invoice.BilledTimeCents);
        Assert.Single(await _vault.Database.TimeEntries.Where(entry => entry.InvoiceId == null).ToListAsync());
    }

    [Fact]
    public async Task RefusesLinesFromAnotherDossierAndWritesNothing()
    {
        var mine = Dossier();
        var other = _vault.Save(new Matter
        {
            Reference = "2026-0008",
            Name = "Ailleurs",
            OpenedOn = new DateOnly(2026, 1, 5),
            HourlyRateCents = 20_000,
        });

        var elsewhere = Heures(other, 60);

        var result = await DropInvoices.FileAsync(
            mine.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_95.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "95", false, false,
                [elsewhere.Id]),
            _vault.Database,
            default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);

        // Refused before anything is written, and before the PDF is copied: a rejected selection
        // must not leave a file filed against a facture that was never completed.
        Assert.Empty(await _vault.Database.Invoices.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(_root, DropInvoices.Folder)));
    }

    [Fact]
    public async Task RefusesLinesAlreadyAttachedToAFacture()
    {
        var matter = Dossier();
        var hours = Heures(matter, 60);

        var first = _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 2, 1),
            AmountExclVatCents = 20_000,
        });

        hours.InvoiceId = first.Id;
        _vault.Database.SaveChanges();

        var result = await DropInvoices.FileAsync(
            matter.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_96.pdf"), null, new DateOnly(2026, 9, 14), 60_000, "96", false, false,
                [hours.Id]),
            _vault.Database,
            default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Single(await _vault.Database.Invoices.ToListAsync());
    }

    /// <summary>
    /// A facture typed in last week, given its PDF and its lines today, ends up worth both passes.
    /// Assigning rather than adding would have dropped whatever it already carried.
    /// </summary>
    [Fact]
    public async Task AddsToWhatAFactureAlreadySettled()
    {
        var matter = Dossier();
        var existing = _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 9, 14),
            AmountExclVatCents = 60_000,
            BilledTimeCents = 20_000,
        });

        var oneHour = Heures(matter, 60);

        await DropInvoices.FileAsync(
            matter.Id,
            new FileInvoiceInput(
                Pdf("FACTURE_97.pdf"), existing.Id, null, null, null, false, false, [oneHour.Id]),
            _vault.Database,
            default);

        Assert.Equal(40_000, (await Reload(existing.Id)).BilledTimeCents);
    }

    // ─── Typed in by hand ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFactureTypedInByHandTakesItsLinesToo()
    {
        var matter = Dossier();
        var threeHours = Heures(matter, 180);

        var result = await CreateInvoice.HandleAsync(
            matter.Id,
            new BillingInvoiceInput(
                new DateOnly(2026, 9, 14), 50_000, "F-2026-014", TimeEntryIds: [threeHours.Id]),
            _vault.Database,
            default);

        Assert.IsAssignableFrom<IValueHttpResult>(result);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        Assert.Equal(60_000, invoice.BilledTimeCents);
        Assert.Equal(-10_000, invoice.VarianceCents);
        Assert.Empty(await _vault.Database.TimeEntries.Where(entry => entry.InvoiceId == null).ToListAsync());
    }

    /// <summary>
    /// Correcting a facture never consumes hours. Fixing a typo in an amount is not the moment to
    /// take lines out of « reste à facturer », and she has no way of knowing that it would.
    /// </summary>
    [Fact]
    public async Task CorrectingAFactureLeavesTheLinesAlone()
    {
        var matter = Dossier();
        var hours = Heures(matter, 60);

        var invoice = _vault.Save(new BillingInvoice
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 9, 14),
            AmountExclVatCents = 50_000,
        });

        await UpdateInvoice.HandleAsync(
            invoice.Id,
            new BillingInvoiceInput(
                new DateOnly(2026, 9, 14), 55_000, "F-2026-014", TimeEntryIds: [hours.Id]),
            _vault.Database,
            default);

        Assert.Equal(55_000, (await Reload(invoice.Id)).AmountExclVatCents);
        Assert.Equal(0, (await Reload(invoice.Id)).BilledTimeCents);
        Assert.Single(await _vault.Database.TimeEntries.Where(entry => entry.InvoiceId == null).ToListAsync());
    }

    // ─── From temps passé, which already worked and must keep working ─────────────────────────────

    [Fact]
    public async Task StillBillsFromTempsPasseAndRecordsTheArbitrage()
    {
        var matter = Dossier();
        var fourHours = Heures(matter, 240);

        await BillTimeEntries.HandleAsync(
            matter.Id,
            new BillTimeInput([fourHours.Id], new DateOnly(2026, 9, 14), 60_000, "F-1"),
            _vault.Database,
            default);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        Assert.Equal(80_000, invoice.BilledTimeCents);
        Assert.Equal(-20_000, invoice.VarianceCents);
    }

    /// <summary>
    /// The same id twice used to pass the count check and bill one line as though it were two. It is
    /// the kind of thing a double click produces.
    /// </summary>
    [Fact]
    public async Task CountsALineOnceWhenItIsSentTwice()
    {
        var matter = Dossier();
        var oneHour = Heures(matter, 60);

        await BillTimeEntries.HandleAsync(
            matter.Id,
            new BillTimeInput([oneHour.Id, oneHour.Id], new DateOnly(2026, 9, 14), null, null),
            _vault.Database,
            default);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        Assert.Equal(20_000, invoice.BilledTimeCents);
        Assert.Equal(20_000, invoice.AmountExclVatCents);
    }

    [Fact]
    public async Task ANonBillableLineIsConsumedAndWorthNothing()
    {
        var matter = Dossier();
        var pro_bono = Heures(matter, 120, billable: false);

        await BillTimeEntries.HandleAsync(
            matter.Id,
            new BillTimeInput([pro_bono.Id], new DateOnly(2026, 9, 14), 10_000, null),
            _vault.Database,
            default);

        var invoice = await _vault.Database.Invoices.AsNoTracking().SingleAsync();

        Assert.Equal(0, invoice.BilledTimeCents);
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
