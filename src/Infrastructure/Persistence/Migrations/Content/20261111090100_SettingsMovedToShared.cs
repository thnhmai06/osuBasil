#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Content;

/// <inheritdoc />
public partial class SettingsMovedToShared : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// The settings table now belongs to the shared context; it stays in the database with its rows.
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
	}
}