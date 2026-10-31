using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Content.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "menu_banners",
			columns: table => new
			{
				image = table.Column<string>(type: "text", nullable: false),
				url = table.Column<string>(type: "text", nullable: false),
				starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_menu_banners", x => x.image);
			});

		migrationBuilder.CreateTable(
			name: "settings",
			columns: table => new
			{
				name = table.Column<string>(type: "text", nullable: false),
				value = table.Column<string>(type: "text", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_settings", x => x.name);
			});
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "menu_banners");

		migrationBuilder.DropTable(
			name: "settings");
	}
}