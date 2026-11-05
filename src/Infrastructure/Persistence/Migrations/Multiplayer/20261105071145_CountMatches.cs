#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Multiplayer;

/// <inheritdoc />
public partial class CountMatches : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.InsertData(
			"multiplayer_id_counters",
			new[] { "name", "next" },
			new object[] { "match_rows", 0L });
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DeleteData(
			"multiplayer_id_counters",
			"name",
			"match_rows");
	}
}