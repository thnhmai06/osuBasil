#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Scores;

/// <inheritdoc />
public partial class CountScores : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.InsertData(
			"score_id_counters",
			new[] { "name", "next" },
			new object[] { "score_rows", 0L });
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DeleteData(
			"score_id_counters",
			"name",
			"score_rows");
	}
}