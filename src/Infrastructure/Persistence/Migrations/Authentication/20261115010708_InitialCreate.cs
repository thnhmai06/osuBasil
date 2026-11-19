#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Authentication;

public partial class InitialCreate : Migration
{
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
			"credentials",
			table => new
			{
				user_id = table.Column<int>(nullable: false),
				password_hash = table.Column<string>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_credentials", x => x.user_id);
				table.ForeignKey(
					"fk_credentials_users_user_id",
					x => x.user_id,
					"users",
					"id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateTable(
			"sessions",
			table => new
			{
				id = table.Column<long>(nullable: false),
				user_id = table.Column<int>(nullable: false),
				type = table.Column<byte>(nullable: false),
				ip = table.Column<string>(maxLength: 45, nullable: false),
				client_info = table.Column<string>(nullable: true),
				started_at = table.Column<DateTimeOffset>(nullable: false),
				last_active_at = table.Column<DateTimeOffset>(nullable: false),
				ended_at = table.Column<DateTimeOffset>(nullable: true),
				end_reason = table.Column<byte>(nullable: true),
				token_hash = table.Column<byte[]>(maxLength: 32, nullable: false),
				expires_at = table.Column<DateTimeOffset>(nullable: true),
				client_date = table.Column<string>(nullable: true),
				client_revision = table.Column<int>(nullable: true),
				client_stream = table.Column<int>(nullable: true),
				osu_path_hash = table.Column<string>(nullable: true),
				network_adapters = table.Column<string>(nullable: true),
				network_adapters_hash = table.Column<string>(nullable: true),
				uninstall_hash = table.Column<string>(nullable: true),
				disk_signature_hash = table.Column<string>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_sessions", x => x.id);
				table.ForeignKey(
					"fk_sessions_users_user_id",
					x => x.user_id,
					"users",
					"id",
					onDelete: ReferentialAction.Cascade);
			});

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

		migrationBuilder.CreateIndex(
			"ix_sessions_token_hash",
			"sessions",
			"token_hash",
			unique: true);

		migrationBuilder.CreateIndex(
			"ix_sessions_user_id_started_at",
			"sessions",
			new[] { "user_id", "started_at" },
			descending: new[] { false, true });
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"connection_id_counters");

		migrationBuilder.DropTable(
			"connections");

		migrationBuilder.DropTable(
			"credentials");

		migrationBuilder.DropTable(
			"sessions");
	}
}