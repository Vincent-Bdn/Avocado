using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterCheckouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matter_checkouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    matter_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    folder_path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    manifest = table.Column<string>(type: "TEXT", nullable: false),
                    opened_at = table.Column<string>(type: "TEXT", nullable: false),
                    synced_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matter_checkouts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matter_checkouts_matter_id",
                table: "matter_checkouts",
                column: "matter_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matter_checkouts");
        }
    }
}
