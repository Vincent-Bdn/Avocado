using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avocado.Server.Data.Migrations
{
    /// <summary>
    /// Repairs the rows that <c>DocumentVersions</c> left unreadable.
    ///
    /// <para>Adding a non-nullable column to a table that already has rows makes SQLite fill them with
    /// the column's default, and EF's default for a string column is the empty string. Timestamps are
    /// stored as ISO-8601 text, so every document that existed before that migration came back with
    /// <c>updated_at = ''</c> — and reading one threw <c>String '' was not recognized as a valid
    /// DateTime</c> before the row ever reached a handler. Deleting a document was enough to hit it.</para>
    ///
    /// <para>The lesson for the next one: a non-nullable column added to a populated table needs a
    /// backfill in the same migration, not a default that happens to be syntactically valid.</para>
    /// </summary>
    public partial class BackfillDocumentTimestamps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The date the file arrived is the honest value: nothing has modified it since.
            migrationBuilder.Sql(
                "UPDATE documents SET updated_at = added_at WHERE updated_at IS NULL OR updated_at = '';");

            // A document that exists has been written once. Zero would render as « v0 ».
            migrationBuilder.Sql("UPDATE documents SET version = 1 WHERE version < 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: this only replaces values that could not be read at all.
        }
    }
}
