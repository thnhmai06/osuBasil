#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Shared;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// The settings table was created by the first migration of Content, so every existing database already has
		// it with its rows; only a database without it gets it here. Never drop or recreate it.
		migrationBuilder.Sql(
			"CREATE TABLE IF NOT EXISTS settings (name varchar(255) NOT NULL, value text NULL, CONSTRAINT pk_settings PRIMARY KEY (name))");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		// The table holds the server's settings and is dropped only by the first migration of Content.
	}
}