#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Shared;

public partial class InitialCreate : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"settings",
			table => new
			{
				name = table.Column<string>(maxLength: 255, nullable: false),
				value = table.Column<string>(nullable: true)
			},
			constraints: table => { table.PrimaryKey("pk_settings", x => x.name); });
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"settings");
	}
}