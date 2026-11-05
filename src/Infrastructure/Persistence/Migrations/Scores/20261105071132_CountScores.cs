using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Scores
{
    /// <inheritdoc />
    public partial class CountScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "score_id_counters",
                columns: new[] { "name", "next" },
                values: new object[] { "score_rows", 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "score_id_counters",
                keyColumn: "name",
                keyValue: "score_rows");
        }
    }
}
