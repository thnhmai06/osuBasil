using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Multiplayer
{
    /// <inheritdoc />
    public partial class CountMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "multiplayer_id_counters",
                columns: new[] { "name", "next" },
                values: new object[] { "match_rows", 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "multiplayer_id_counters",
                keyColumn: "name",
                keyValue: "match_rows");
        }
    }
}
