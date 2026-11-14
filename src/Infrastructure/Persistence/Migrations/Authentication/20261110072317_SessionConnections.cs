#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Authentication;

/// <inheritdoc />
public partial class SessionConnections : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"connection_id_counters",
			table => new
			{
				name = table.Column<string>(maxLength: 64, nullable: false),
				next = table.Column<long>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_connection_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"connections",
			table => new
			{
				id = table.Column<long>(nullable: false),
				session_id = table.Column<long>(nullable: false),
				ip = table.Column<string>(maxLength: 45, nullable: false),
				client_info = table.Column<string>(nullable: true),
				opened_at = table.Column<DateTimeOffset>(nullable: false),
				last_active_at = table.Column<DateTimeOffset>(nullable: false),
				closed_at = table.Column<DateTimeOffset>(nullable: true),
				close_reason = table.Column<byte>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_connections", x => x.id);
				table.ForeignKey(
					"fk_connections_sessions_session_id",
					x => x.session_id,
					"sessions",
					"id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.InsertData(
			"connection_id_counters",
			new[] { "name", "next" },
			new object[] { "connection", 1L });

		migrationBuilder.CreateIndex(
			"ix_connections_closed_at",
			"connections",
			"closed_at");

		migrationBuilder.CreateIndex(
			"ix_connections_session_id_opened_at",
			"connections",
			new[] { "session_id", "opened_at" },
			descending: new[] { false, true });
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"connection_id_counters");

		migrationBuilder.DropTable(
			"connections");
	}
}