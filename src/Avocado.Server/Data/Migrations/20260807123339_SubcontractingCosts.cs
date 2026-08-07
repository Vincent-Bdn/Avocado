using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubcontractingCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    label = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    contact_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    amount_excl_vat_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    external_reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    is_paid = table.Column<bool>(type: "INTEGER", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    invoice_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_costs", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_costs_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_billing_costs_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_billing_costs_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_costs_contact_id",
                table: "billing_costs",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_costs_invoice_id",
                table: "billing_costs",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_costs_matter_id_date",
                table: "billing_costs",
                columns: new[] { "matter_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_costs");
        }
    }
}
