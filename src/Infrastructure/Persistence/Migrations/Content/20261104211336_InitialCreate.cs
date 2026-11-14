#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Content;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"menu_banners",
			table => new
			{
				image = table.Column<string>(maxLength: 255, nullable: false),
				url = table.Column<string>(nullable: false),
				starts_at = table.Column<DateTimeOffset>(nullable: true),
				ends_at = table.Column<DateTimeOffset>(nullable: true),
				created_at = table.Column<DateTimeOffset>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_menu_banners", x => x.image); });

		migrationBuilder.CreateTable(
			"settings",
			table => new
			{
				name = table.Column<string>(maxLength: 255, nullable: false),
				value = table.Column<string>(nullable: true)
			},
			constraints: table => { table.PrimaryKey("pk_settings", x => x.name); });
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"menu_banners");

		migrationBuilder.DropTable(
			"settings");
	}
}