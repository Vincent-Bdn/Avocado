using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    civility = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    last_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    first_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    legal_name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    siren = table.Column<string>(type: "TEXT", maxLength: 14, nullable: true),
                    legal_form = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    address = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "matters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    opened_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    hourly_rate_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    court_case_number = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    hourly_rate_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deadlines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    time = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    remind_days_before = table.Column<int>(type: "INTEGER", nullable: false),
                    is_done = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deadlines", x => x.id);
                    table.ForeignKey(
                        name: "FK_deadlines_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount_excl_vat_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    external_reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    is_paid = table.Column<bool>(type: "INTEGER", nullable: false),
                    paid_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoices_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount_cents = table.Column<long>(type: "INTEGER", nullable: false),
                    label = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_ledger_entries_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matter_parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contact_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_client = table.Column<bool>(type: "INTEGER", nullable: false),
                    role = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matter_parties", x => x.id);
                    table.ForeignKey(
                        name: "FK_matter_parties_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_matter_parties_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    contact_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    subject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    body = table.Column<string>(type: "TEXT", nullable: true),
                    tracking_number = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_activities_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_activities_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_activities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    activity_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    blob_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    document_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    added_at = table.Column<string>(type: "TEXT", nullable: false),
                    exhibit_number = table.Column<int>(type: "INTEGER", nullable: true),
                    exhibit_label = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_documents_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_documents_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    started_at = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    duration_minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    task = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    is_billable = table.Column<bool>(type: "INTEGER", nullable: false),
                    hourly_rate_cents_override = table.Column<long>(type: "INTEGER", nullable: true),
                    activity_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_time_entries_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_time_entries_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_time_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activities_contact_id",
                table: "activities",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_activities_matter_id_occurred_at",
                table: "activities",
                columns: new[] { "matter_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_activities_user_id",
                table: "activities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_contacts_last_name",
                table: "contacts",
                column: "last_name");

            migrationBuilder.CreateIndex(
                name: "IX_contacts_legal_name",
                table: "contacts",
                column: "legal_name");

            migrationBuilder.CreateIndex(
                name: "IX_contacts_siren",
                table: "contacts",
                column: "siren");

            migrationBuilder.CreateIndex(
                name: "IX_deadlines_is_done_date",
                table: "deadlines",
                columns: new[] { "is_done", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_deadlines_matter_id_is_done_date",
                table: "deadlines",
                columns: new[] { "matter_id", "is_done", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_activity_id",
                table: "documents",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_blob_sha256",
                table: "documents",
                column: "blob_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_documents_matter_id",
                table: "documents",
                column: "matter_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_matter_id_exhibit_number",
                table: "documents",
                columns: new[] { "matter_id", "exhibit_number" },
                unique: true,
                filter: "exhibit_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_is_paid",
                table: "invoices",
                column: "is_paid");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_matter_id_date",
                table: "invoices",
                columns: new[] { "matter_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_matter_id_date",
                table: "ledger_entries",
                columns: new[] { "matter_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_matter_parties_contact_id",
                table: "matter_parties",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_matter_parties_matter_id_contact_id",
                table: "matter_parties",
                columns: new[] { "matter_id", "contact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_matters_closed_on",
                table: "matters",
                column: "closed_on");

            migrationBuilder.CreateIndex(
                name: "IX_matters_court_case_number",
                table: "matters",
                column: "court_case_number");

            migrationBuilder.CreateIndex(
                name: "IX_matters_reference",
                table: "matters",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_activity_id",
                table: "time_entries",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_date",
                table: "time_entries",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_matter_id_date",
                table: "time_entries",
                columns: new[] { "matter_id", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_user_id",
                table: "time_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_is_active",
                table: "users",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deadlines");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "matter_parties");

            migrationBuilder.DropTable(
                name: "time_entries");

            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "matters");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
