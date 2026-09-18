using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class FactureEnPdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_path",
                table: "invoices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "document_path",
                table: "invoices");
        }
    }
}
