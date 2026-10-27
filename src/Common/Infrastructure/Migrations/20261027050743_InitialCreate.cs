using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Common.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "write_tickets",
			columns: table => new
			{
				id = table.Column<Guid>(type: "uuid", nullable: false),
				created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_write_tickets", x => x.id);
			});

		migrationBuilder.CreateIndex(
			name: "ix_write_tickets_created_at",
			table: "write_tickets",
			column: "created_at");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "write_tickets");
	}
}