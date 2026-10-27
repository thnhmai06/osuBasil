using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Users;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "users",
			columns: table => new
			{
				id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:IdentitySequenceOptions", "'2', '1', '', '', 'False', '1'")
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				name = table.Column<string>(type: "text", nullable: false),
				normalized_name = table.Column<string>(type: "text", nullable: false),
				country = table.Column<int>(type: "integer", nullable: false),
				permissions = table.Column<long>(type: "bigint", nullable: false),
				deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_users", x => x.id);
				table.CheckConstraint("users_id_check", "id >= 1");
			});

		migrationBuilder.CreateTable(
			name: "moderation_log",
			columns: table => new
			{
				id = table.Column<long>(type: "bigint", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
				action = table.Column<int>(type: "integer", nullable: false),
				by_id = table.Column<int>(type: "integer", nullable: false),
				target_id = table.Column<int>(type: "integer", nullable: false),
				reason = table.Column<string>(type: "text", nullable: true),
				timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_moderation_log", x => x.id);
				table.ForeignKey(
					name: "fk_moderation_log_users_by_id",
					column: x => x.by_id,
					principalTable: "users",
					principalColumn: "id");
				table.ForeignKey(
					name: "fk_moderation_log_users_target_id",
					column: x => x.target_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "relationships",
			columns: table => new
			{
				actor_id = table.Column<int>(type: "integer", nullable: false),
				target_id = table.Column<int>(type: "integer", nullable: false),
				type = table.Column<int>(type: "integer", nullable: false),
				created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_relationships", x => new { x.actor_id, x.target_id });
				table.ForeignKey(
					name: "fk_relationships_users_actor_id",
					column: x => x.actor_id,
					principalTable: "users",
					principalColumn: "id");
				table.ForeignKey(
					name: "fk_relationships_users_target_id",
					column: x => x.target_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "restrictions",
			columns: table => new
			{
				id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				user_id = table.Column<int>(type: "integer", nullable: false),
				permissions = table.Column<long>(type: "bigint", nullable: false),
				starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_restrictions", x => x.id);
				table.CheckConstraint("restrictions_id_check", "id >= 1");
				table.ForeignKey(
					name: "fk_restrictions_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.CreateIndex(
			name: "ix_moderation_log_by_id",
			table: "moderation_log",
			column: "by_id");

		migrationBuilder.CreateIndex(
			name: "ix_moderation_log_target_id_timestamp",
			table: "moderation_log",
			columns: ["target_id", "timestamp"],
			descending: [false, true]);

		migrationBuilder.CreateIndex(
			name: "ix_relationships_target_id",
			table: "relationships",
			column: "target_id");

		migrationBuilder.CreateIndex(
			name: "ix_restrictions_user_id",
			table: "restrictions",
			column: "user_id");

		migrationBuilder.CreateIndex(
			name: "ix_users_normalized_name",
			table: "users",
			column: "normalized_name",
			unique: true,
			filter: "deleted_at is null");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "moderation_log");

		migrationBuilder.DropTable(
			name: "relationships");

		migrationBuilder.DropTable(
			name: "restrictions");

		migrationBuilder.DropTable(
			name: "users");
	}
}