using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backup_destinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    volume_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    secret = table.Column<string>(type: "TEXT", nullable: true),
                    remote_folder_id = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    keep_newest = table.Column<int>(type: "INTEGER", nullable: false),
                    keep_daily_for_days = table.Column<int>(type: "INTEGER", nullable: false),
                    last_backup_at = table.Column<string>(type: "TEXT", nullable: true),
                    last_seen_at = table.Column<string>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_destinations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_destinations_is_enabled",
                table: "backup_destinations",
                column: "is_enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_destinations");
        }
    }
}
