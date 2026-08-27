using Avocado.Server.Data;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.ValueObjects;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// « Reste à facturer » against a real database rather than against its arithmetic.
///
/// <para>The arithmetic was never wrong. What was wrong was which factures were fed to it, and that
/// decision lives in a LINQ filter that <see cref="BillingSummaryTests"/> cannot reach. A dossier
/// imported from Gestisoft with seven paid factures reported « reste à facturer − 8 974 € », and the
/// first 45 minutes recorded on it made that − 8 794 €.</para>
///
/// <para>Unencrypted SQLite in memory, since what is being tested is a query and not a vault.</para>
/// </summary>
public class BillingSummaryQueryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly AvocadoDbContext _database;
    private readonly Matter _matter;

    public BillingSummaryQueryTests()
    {
        _connection.Open();

        _database = new AvocadoDbContext(
            new DbContextOptionsBuilder<AvocadoDbContext>().UseSqlite(_connection).Options);

        _database.Database.EnsureCreated();

        _matter = new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2025, 1, 6),
            HourlyRateCents = 24_000,
        };

        _database.Matters.Add(_matter);
        _database.SaveChanges();
    }

    private void Invoice(long amountExclVatCents, bool historical = false, long billedTimeCents = 0)
    {
        _database.Invoices.Add(new BillingInvoice
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2025, 7, 3),
            AmountExclVatCents = amountExclVatCents,
            BilledTimeCents = billedTimeCents,
            IsHistorical = historical,
        });

        _database.SaveChanges();
    }

    private void Minutes(int minutes)
    {
        _database.TimeEntries.Add(new TimeEntry
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 8, 27),
            DurationMinutes = minutes,
            Task = "Diligences",
            IsBillable = true,
        });

        _database.SaveChanges();
    }

    private BillingSummary Summary() =>
        BillingSummaryQuery.ForMatterAsync(_database, _matter.Id, default).GetAwaiter().GetResult();

    /// <summary>
    /// The one that was wrong. Seven factures brought over from Gestisoft, 8 974 € in all, and 45
    /// minutes recorded afterwards: what is left to bill is those 45 minutes and nothing else.
    /// </summary>
    [Fact]
    public void AHistoricalFactureDoesNotEatIntoWhatIsLeftToBill()
    {
        foreach (var amount in new long[] { 260_000, 100_000, 291_000, 128_000, 15_000, 50_000, 53_400 })
        {
            Invoice(amount, historical: true);
        }

        Assert.Equal(0, Summary().LeftToBillCents);

        Minutes(45);

        var summary = Summary();

        Assert.Equal(18_000, summary.LeftToBillCents);
        Assert.Equal(897_400, summary.InvoicedCents);
        Assert.Equal(0, summary.ManualInvoicedCents);
    }

    /// <summary>
    /// And the case the subtraction is there for, which must keep working: she records that she
    /// invoiced 600 €, the hours justifying it are still sitting unbilled, and counting both would
    /// bill the same work twice.
    /// </summary>
    [Fact]
    public void AFactureRecordedByHandStillEatsIntoIt()
    {
        Minutes(300);
        Invoice(60_000);

        var summary = Summary();

        Assert.Equal(120_000, summary.BillableTimeCents);
        Assert.Equal(60_000, summary.ManualInvoicedCents);
        Assert.Equal(60_000, summary.LeftToBillCents);
    }

    /// <summary>A facture built from selected hours consumed them, so it is not subtracted either.</summary>
    [Fact]
    public void AFactureBuiltFromHoursIsNotSubtractedTwice()
    {
        Minutes(300);
        Invoice(60_000, billedTimeCents: 50_000);

        var summary = Summary();

        Assert.Equal(0, summary.ManualInvoicedCents);
        Assert.Equal(120_000, summary.LeftToBillCents);
        Assert.Equal(10_000, summary.VarianceCents);
    }

    /// <summary>Historical or not, it was billed, and « facturé » and « net » say so.</summary>
    [Fact]
    public void AHistoricalFactureStillCountsAsInvoiced()
    {
        Invoice(260_000, historical: true);

        var summary = Summary();

        Assert.Equal(260_000, summary.InvoicedCents);
        Assert.Equal(260_000, summary.NetCents);
        Assert.Equal(0, summary.VarianceCents);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _database.Dispose();
        _connection.Dispose();
    }
}
