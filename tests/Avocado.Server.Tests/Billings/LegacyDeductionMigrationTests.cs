using Avocado.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// The day ten practices update.
///
/// <para>Factures used not to consume the lines they billed, so a hand-recorded one subtracted its
/// amount from « reste à facturer » instead. Under the new model it consumes nothing and subtracts
/// nothing, which would make every dossier carrying one jump up by that amount overnight, and which
/// hours it actually paid for cannot be worked out after the fact.</para>
///
/// <para>So the migration marks exactly the factures that were subtracting the day before, and they
/// go on doing it. This runs the real migrations, forwards, against the shape the data was actually
/// in: nothing else can show that the flag lands on the right rows.</para>
/// </summary>
public sealed class LegacyDeductionMigrationTests : IDisposable
{
    /// <summary>The last migration before factures took their lines with them.</summary>
    private const string BeforeTheChange = "20260917155156_ForfaitConvenu";

    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public LegacyDeductionMigrationTests() => _connection.Open();

    private AvocadoDbContext Context() =>
        new(new DbContextOptionsBuilder<AvocadoDbContext>().UseSqlite(_connection).Options);

    private static void MigrateTo(AvocadoDbContext context, string? target) =>
        context.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(target);

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void MarksExactlyTheFacturesThatWereAlreadySubtracting()
    {
        using (var old = Context())
        {
            MigrateTo(old, BeforeTheChange);
        }

        // A dossier, and the three kinds of facture that existed the day before.
        Execute(_connection, """
            INSERT INTO matters (id, reference, name, opened_on, hourly_rate_cents, is_favourite, created_at, updated_at)
            VALUES ('11111111-1111-1111-1111-111111111111', '700770', 'COULEYRE', '2025-01-06', 24000, 0, '2025-01-06T00:00:00+00:00', '2025-01-06T00:00:00+00:00');

            INSERT INTO invoices (id, matter_id, date, amount_excl_vat_cents, billed_time_cents, is_historical, is_paid, created_at)
            VALUES
              -- recorded by hand: was subtracting, and must go on subtracting
              ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', '2026-03-01', 60000, 0, 0, 0, '2026-03-01T00:00:00+00:00'),
              -- built from selected hours: consumed them, never subtracted
              ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', '2026-04-01', 80000, 75000, 0, 0, '2026-04-01T00:00:00+00:00'),
              -- a reprise: never subtracted either
              ('44444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', '2025-02-01', 260000, 0, 1, 1, '2025-02-01T00:00:00+00:00');
            """);

        using (var upgraded = Context())
        {
            MigrateTo(upgraded, null);
        }

        Assert.Equal(
            1,
            Scalar(_connection, "SELECT COUNT(*) FROM invoices WHERE deducts_without_lines = 1"));

        Assert.Equal(
            1,
            Scalar(
                _connection,
                "SELECT deducts_without_lines FROM invoices WHERE id = '22222222-2222-2222-2222-222222222222'"));

        // The one built from hours already had its lines; flagging it would subtract the same work twice.
        Assert.Equal(
            0,
            Scalar(
                _connection,
                "SELECT deducts_without_lines FROM invoices WHERE id = '33333333-3333-3333-3333-333333333333'"));

        // And the reprise never subtracted anything. COULEYRE's seven of them are why.
        Assert.Equal(
            0,
            Scalar(
                _connection,
                "SELECT deducts_without_lines FROM invoices WHERE id = '44444444-4444-4444-4444-444444444444'"));
    }

    /// <summary>A vault created today has nothing to carry forward, and must not invent any.</summary>
    [Fact]
    public void LeavesANewVaultWithNoneAtAll()
    {
        using var fresh = Context();
        MigrateTo(fresh, null);

        Assert.Equal(0, Scalar(_connection, "SELECT COUNT(*) FROM invoices WHERE deducts_without_lines = 1"));
    }

    public void Dispose() => _connection.Dispose();
}
