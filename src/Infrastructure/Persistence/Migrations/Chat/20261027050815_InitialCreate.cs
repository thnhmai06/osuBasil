using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Chat;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "channels",
			columns: table => new
			{
				name = table.Column<string>(type: "text", nullable: false),
				topic = table.Column<string>(type: "text", nullable: false),
				read_privileges = table.Column<int>(type: "integer", nullable: false),
				write_privileges = table.Column<int>(type: "integer", nullable: false),
				auto_join = table.Column<bool>(type: "boolean", nullable: false),
				visible = table.Column<bool>(type: "boolean", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_channels", x => x.name);
			});

		migrationBuilder.InsertData(
			table: "channels",
			columns: ["name", "auto_join", "read_privileges", "topic", "visible", "write_privileges"],
			values: new object[,]
			{
				{ "#lobby", false, 0, "Multiplayer lobby discussion.", true, 0 },
				{ "#osu", true, 0, "General discussion.", true, 0 }
			});
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "channels");
	}
}