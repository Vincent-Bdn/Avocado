using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupSubpath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "subpath",
                table: "backup_destinations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subpath",
                table: "backup_destinations");
        }
    }
}
