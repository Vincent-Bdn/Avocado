using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class TheSauvegardeCarriesTheDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "captured_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    blob_sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    modified_at = table.Column<string>(type: "TEXT", nullable: false),
                    captured_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_captured_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_captured_files_matters_matter_id",
                        column: x => x.matter_id,
                        principalTable: "matters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_captured_files_matter_id_relative_path",
                table: "captured_files",
                columns: new[] { "matter_id", "relative_path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "captured_files");
        }
    }
}
