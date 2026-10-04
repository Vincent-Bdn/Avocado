using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// « Sur ce dossier j'ai 1 500 en temps passé, j'ai facturé 1 000 et ne pourrai pas facturer plus. »
///
/// <para>Without somewhere to put that, those hours sit in « reste à facturer » for ever and the
/// dossier never settles, which is what a lawyer six months into Avocado reported. An abandon is the
/// same act as billing with nothing billed: it consumes its lines, it is not turnover, and the whole
/// of what those lines were worth becomes a mali.</para>
/// </summary>
public class AbandonTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly Matter _matter;

    public AbandonTests() =>
        _matter = _vault.Save(new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2025, 1, 6),
            HourlyRateCents = 24_000,
        });

    private TimeEntry Minutes(int minutes) =>
        _vault.Save(new TimeEntry
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 8, 27),
            DurationMinutes = minutes,
            Task = "Diligences",
            IsBillable = true,
        });

    private Task<IResult> AbandonAsync(params TimeEntry[] entries) =>
        BillTimeEntries.HandleAsync(
            _matter.Id,
            new BillTimeInput(
                [.. entries.Select(entry => entry.Id)],
                new DateOnly(2026, 9, 30),
                AmountExclVatCents: null,
                ExternalReference: null,
                IsAbandon: true),
            _vault.Database,
            default);

    [Fact]
    public async Task TakesTheHoursOutAndRecordsThemAsAMali()
    {
        var kept = Minutes(60);
        var written = Minutes(300);

        Assert.Equal(201, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await AbandonAsync(written)).StatusCode);

        var abandon = await _vault.Database.Invoices.SingleAsync();

        Assert.True(abandon.IsAbandon);
        Assert.Equal(0, abandon.AmountExclVatCents);
        Assert.Equal(120_000, abandon.BilledTimeCents);
        Assert.Equal(-120_000, abandon.VarianceCents);

        var summary = await BillingSummaryQuery.ForMatterAsync(_vault.Database, _matter.Id, default);

        // The hour that was not written off is all that is left to bill, and the abandon is nowhere
        // in « facturé »: she gave those hours away, she did not invoice them.
        Assert.Equal(24_000, summary.LeftToBillCents);
        Assert.Equal(60, summary.UnbilledMinutes);
        Assert.Equal(0, summary.InvoicedCents);
        Assert.Equal(-120_000, summary.VarianceCents);

        // And the kept line is untouched, which is the thing a mistake here would silently break.
        await _vault.Database.Entry(kept).ReloadAsync();
        Assert.Null(kept.InvoiceId);
    }

    /// <summary>
    /// Nothing was billed, so nothing can have been paid, and an amount sent alongside is ignored
    /// rather than rejected: the dialog has no field for either and could not correct itself.
    /// </summary>
    [Fact]
    public async Task IgnoresAnAmountAndAPaymentSentWithIt()
    {
        var written = Minutes(300);

        var result = await BillTimeEntries.HandleAsync(
            _matter.Id,
            new BillTimeInput(
                [written.Id],
                new DateOnly(2026, 9, 30),
                AmountExclVatCents: 500_000,
                ExternalReference: "F-2026-099",
                IsPaid: true,
                IsAbandon: true),
            _vault.Database,
            default);

        Assert.Equal(201, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

        var abandon = await _vault.Database.Invoices.SingleAsync();

        Assert.Equal(0, abandon.AmountExclVatCents);
        Assert.False(abandon.IsPaid);
        Assert.Null(abandon.PaidOn);
    }

    /// <summary>
    /// Deleting it hands the hours back, through the same foreign key a facture uses. « J'ai soldé
    /// le mauvais dossier » has to be undoable, and a figure that cannot be corrected is a figure
    /// that gets worked around in a spreadsheet.
    /// </summary>
    [Fact]
    public async Task GivesTheHoursBackWhenItIsDeleted()
    {
        var written = Minutes(300);

        await AbandonAsync(written);

        var abandon = await _vault.Database.Invoices.SingleAsync();

        await DeleteBillingRecord.InvoiceAsync(abandon.Id, _vault.Database, default);

        var summary = await BillingSummaryQuery.ForMatterAsync(_vault.Database, _matter.Id, default);

        Assert.Equal(120_000, summary.LeftToBillCents);
        Assert.Equal(0, summary.VarianceCents);
    }

    /// <summary>At a forfait it changes no figure, which is the whole point of agreeing a price.</summary>
    [Fact]
    public async Task LeavesAForfaitAlone()
    {
        _matter.FixedFeeCents = 300_000;
        await _vault.Database.SaveChangesAsync();

        await AbandonAsync(Minutes(300));

        var summary = await BillingSummaryQuery.ForMatterAsync(_vault.Database, _matter.Id, default);

        Assert.Equal(300_000, summary.LeftToBillCents);
        Assert.Equal(-120_000, summary.VarianceCents);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}
