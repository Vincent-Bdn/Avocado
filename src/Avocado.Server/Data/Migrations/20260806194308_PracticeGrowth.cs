using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class PracticeGrowth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "classification",
                table: "matters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "court",
                table: "matters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_favourite",
                table: "matters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "folder",
                table: "documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attached_as",
                table: "contacts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attached_to_contact_id",
                table: "contacts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "practice_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_attached_to_contact_id",
                table: "contacts",
                column: "attached_to_contact_id");

            migrationBuilder.AddForeignKey(
                name: "FK_contacts_contacts_attached_to_contact_id",
                table: "contacts",
                column: "attached_to_contact_id",
                principalTable: "contacts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contacts_contacts_attached_to_contact_id",
                table: "contacts");

            migrationBuilder.DropTable(
                name: "practice_settings");

            migrationBuilder.DropIndex(
                name: "IX_contacts_attached_to_contact_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "classification",
                table: "matters");

            migrationBuilder.DropColumn(
                name: "court",
                table: "matters");

            migrationBuilder.DropColumn(
                name: "is_favourite",
                table: "matters");

            migrationBuilder.DropColumn(
                name: "folder",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "attached_as",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "attached_to_contact_id",
                table: "contacts");
        }
    }
}
