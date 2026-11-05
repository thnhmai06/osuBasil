#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Chat;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"channels",
			table => new
			{
				name = table.Column<string>(maxLength: 255, nullable: false),
				topic = table.Column<string>(nullable: false),
				read_privileges = table.Column<int>(nullable: false),
				write_privileges = table.Column<int>(nullable: false),
				auto_join = table.Column<bool>(nullable: false),
				visible = table.Column<bool>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_channels", x => x.name); });

		migrationBuilder.InsertData(
			"channels",
			new[] { "name", "auto_join", "read_privileges", "topic", "visible", "write_privileges" },
			new object[,]
			{
				{ "#lobby", false, 0, "Multiplayer lobby discussion.", true, 0 },
				{ "#osu", true, 0, "General discussion.", true, 0 }
			});
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"channels");
	}
}