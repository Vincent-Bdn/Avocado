using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutAwaitingDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "awaiting_decision",
                table: "matter_checkouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "awaiting_decision",
                table: "matter_checkouts");
        }
    }
}
