using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Auth.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "credentials",
			columns: table => new
			{
				user_id = table.Column<int>(type: "integer", nullable: false),
				password_hash = table.Column<string>(type: "text", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_credentials", x => x.user_id);
				table.ForeignKey(
					name: "fk_credentials_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateTable(
			name: "sessions",
			columns: table => new
			{
				id = table.Column<Guid>(type: "uuid", nullable: false),
				user_id = table.Column<int>(type: "integer", nullable: false),
				type = table.Column<short>(type: "smallint", nullable: false),
				ip = table.Column<IPAddress>(type: "inet", nullable: false),
				client_info = table.Column<string>(type: "text", nullable: true),
				started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				end_reason = table.Column<short>(type: "smallint", nullable: true),
				token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
				expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				must_reauthenticate = table.Column<bool>(type: "boolean", nullable: false),
				client_date = table.Column<string>(type: "text", nullable: true),
				client_revision = table.Column<int>(type: "integer", nullable: true),
				client_stream = table.Column<int>(type: "integer", nullable: true),
				osu_path_hash = table.Column<string>(type: "text", nullable: true),
				network_adapters = table.Column<string>(type: "text", nullable: true),
				network_adapters_hash = table.Column<string>(type: "text", nullable: true),
				uninstall_hash = table.Column<string>(type: "text", nullable: true),
				disk_signature_hash = table.Column<string>(type: "text", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_sessions", x => x.id);
				table.ForeignKey(
					name: "fk_sessions_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateIndex(
			name: "ix_sessions_token_hash",
			table: "sessions",
			column: "token_hash");

		migrationBuilder.CreateIndex(
			name: "ix_sessions_user_id_started_at",
			table: "sessions",
			columns: ["user_id", "started_at"],
			descending: [false, true]);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "credentials");

		migrationBuilder.DropTable(
			name: "sessions");
	}
}
