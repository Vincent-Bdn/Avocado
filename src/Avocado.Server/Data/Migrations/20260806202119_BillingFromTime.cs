using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillingFromTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "invoice_id",
                table: "time_entries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "billed_time_cents",
                table: "invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "document_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    blob_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_invoice_id",
                table: "time_entries",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_templates_kind",
                table: "document_templates",
                column: "kind");

            migrationBuilder.AddForeignKey(
                name: "FK_time_entries_invoices_invoice_id",
                table: "time_entries",
                column: "invoice_id",
                principalTable: "invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_time_entries_invoices_invoice_id",
                table: "time_entries");

            migrationBuilder.DropTable(
                name: "document_templates");

            migrationBuilder.DropIndex(
                name: "IX_time_entries_invoice_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                table: "time_entries");

            migrationBuilder.DropColumn(
                name: "billed_time_cents",
                table: "invoices");
        }
    }
}
