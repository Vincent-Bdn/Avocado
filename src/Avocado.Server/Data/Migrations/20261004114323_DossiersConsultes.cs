using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DossiersConsultes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_viewed_at",
                table: "matters",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_viewed_at",
                table: "matters");
        }
    }
}
