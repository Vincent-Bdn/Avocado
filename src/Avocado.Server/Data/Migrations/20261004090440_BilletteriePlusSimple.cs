using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class BilletteriePlusSimple : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deducts_without_lines",
                table: "invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_abandon",
                table: "invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Every facture that was subtracting from « reste a facturer » the day before this ran
            // goes on subtracting. Ten practices were already working: their hand-recorded factures
            // consumed no lines, because there was no way to attach any, and which hours each one
            // actually paid for is not knowable now. Without this, every dossier carrying one jumps
            // up by that amount overnight.
            //
            // The condition is the old rule verbatim: a facture reduced « reste a facturer » when it
            // carried no billed time and was not a reprise. Nothing written from here on ever sets
            // this flag; see BillingInvoice.DeductsWithoutLines.
            migrationBuilder.Sql(
                """
                UPDATE invoices
                   SET deducts_without_lines = 1
                 WHERE billed_time_cents = 0
                   AND is_historical = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deducts_without_lines",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "is_abandon",
                table: "invoices");
        }
    }
}
