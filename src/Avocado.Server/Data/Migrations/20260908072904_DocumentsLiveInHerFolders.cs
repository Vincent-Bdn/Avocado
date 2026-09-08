using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsLiveInHerFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "matter_checkouts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    activity_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    added_at = table.Column<string>(type: "TEXT", nullable: false),
                    blob_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    document_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    exhibit_label = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    exhibit_number = table.Column<int>(type: "INTEGER", nullable: true),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    folder = table.Column<string>(type: "TEXT", nullable: true),
                    mime_type = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "matter_checkouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    awaiting_decision = table.Column<bool>(type: "INTEGER", nullable: false),
                    folder_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    manifest = table.Column<string>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    opened_at = table.Column<string>(type: "TEXT", nullable: false),
                    synced_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matter_checkouts", x => x.id);
                });

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
                name: "IX_matter_checkouts_matter_id",
                table: "matter_checkouts",
                column: "matter_id",
                unique: true);
        }
    }
}
