#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Authentication;

/// <inheritdoc />
public partial class SessionIdAndTokenHash : Migration
{
	// A session's id changes type and its token hash becomes required, so the table is rebuilt: the stored sessions
	// are dropped and everyone signs in again.

	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"sessions");

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

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"sessions");

		migrationBuilder.CreateTable(
			"sessions",
			table => new
			{
				id = table.Column<Guid>(nullable: false),
				user_id = table.Column<int>(nullable: false),
				type = table.Column<byte>(nullable: false),
				ip = table.Column<string>(maxLength: 45, nullable: false),
				client_info = table.Column<string>(nullable: true),
				started_at = table.Column<DateTimeOffset>(nullable: false),
				last_active_at = table.Column<DateTimeOffset>(nullable: false),
				ended_at = table.Column<DateTimeOffset>(nullable: true),
				end_reason = table.Column<byte>(nullable: true),
				token_hash = table.Column<byte[]>(maxLength: 64, nullable: true),
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
}
