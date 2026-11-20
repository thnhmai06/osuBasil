#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Sessions;

/// <inheritdoc />
public partial class MergeConnectionsIntoSessions : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"connection_id_counters");

		migrationBuilder.DropTable(
			"connections");

		migrationBuilder.DropColumn(
			"expires_at",
			"sessions");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<DateTimeOffset>(
			"expires_at",
			"sessions",
			"timestamp with time zone",
			nullable: true);

		migrationBuilder.CreateTable(
			"connection_id_counters",
			table => new
			{
				name = table.Column<string>("character varying(64)", maxLength: 64, nullable: false),
				next = table.Column<long>("bigint", nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_connection_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"connections",
			table => new
			{
				id = table.Column<long>("bigint", nullable: false),
				client_info = table.Column<string>("text", nullable: true),
				close_reason = table.Column<byte>("smallint", nullable: true),
				closed_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: true),
				ip = table.Column<string>("character varying(45)", maxLength: 45, nullable: false),
				last_active_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
				opened_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
				session_id = table.Column<long>("bigint", nullable: false)
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
}