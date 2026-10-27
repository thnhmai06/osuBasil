using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Auth;

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
			name: "logins",
			columns: table => new
			{
				id = table.Column<long>(type: "bigint", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
				user_id = table.Column<int>(type: "integer", nullable: false),
				ip = table.Column<IPAddress>(type: "inet", nullable: false),
				timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
				table.PrimaryKey("pk_logins", x => x.id);
				table.ForeignKey(
					name: "fk_logins_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateIndex(
			name: "ix_logins_user_id_timestamp",
			table: "logins",
			columns: ["user_id", "timestamp"],
			descending: [false, true]);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "credentials");

		migrationBuilder.DropTable(
			name: "logins");
	}
}