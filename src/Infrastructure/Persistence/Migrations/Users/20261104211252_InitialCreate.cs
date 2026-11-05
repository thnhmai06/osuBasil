#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Users;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"user_id_counters",
			table => new
			{
				name = table.Column<string>(maxLength: 64, nullable: false),
				next = table.Column<long>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_user_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"users",
			table => new
			{
				id = table.Column<int>(nullable: false),
				name = table.Column<string>(nullable: false),
				normalized_name = table.Column<string>(maxLength: 255, nullable: false),
				active_name = table.Column<string>(maxLength: 255, nullable: true),
				country = table.Column<int>(nullable: false),
				permissions = table.Column<long>(nullable: false),
				deleted_at = table.Column<DateTimeOffset>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_users", x => x.id);
				table.CheckConstraint("users_id_check", "id >= 1");
			});

		migrationBuilder.CreateTable(
			"moderation_log",
			table => new
			{
				id = table.Column<long>(nullable: false),
				action = table.Column<int>(nullable: false),
				by_id = table.Column<int>(nullable: false),
				target_id = table.Column<int>(nullable: false),
				reason = table.Column<string>(nullable: true),
				timestamp = table.Column<DateTimeOffset>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_moderation_log", x => x.id);
				table.ForeignKey(
					"fk_moderation_log_users_by_id",
					x => x.by_id,
					"users",
					"id");
				table.ForeignKey(
					"fk_moderation_log_users_target_id",
					x => x.target_id,
					"users",
					"id");
			});

		migrationBuilder.CreateTable(
			"relationships",
			table => new
			{
				actor_id = table.Column<int>(nullable: false),
				target_id = table.Column<int>(nullable: false),
				type = table.Column<int>(nullable: false),
				created_at = table.Column<DateTimeOffset>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_relationships", x => new { x.actor_id, x.target_id });
				table.ForeignKey(
					"fk_relationships_users_actor_id",
					x => x.actor_id,
					"users",
					"id");
				table.ForeignKey(
					"fk_relationships_users_target_id",
					x => x.target_id,
					"users",
					"id");
			});

		migrationBuilder.CreateTable(
			"restrictions",
			table => new
			{
				id = table.Column<int>(nullable: false),
				user_id = table.Column<int>(nullable: false),
				permissions = table.Column<long>(nullable: false),
				starts_at = table.Column<DateTimeOffset>(nullable: false),
				ends_at = table.Column<DateTimeOffset>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_restrictions", x => x.id);
				table.CheckConstraint("restrictions_id_check", "id >= 1");
				table.ForeignKey(
					"fk_restrictions_users_user_id",
					x => x.user_id,
					"users",
					"id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.InsertData(
			"user_id_counters",
			new[] { "name", "next" },
			new object[,]
			{
				{ "moderation_log", 1L },
				{ "restriction", 1L },
				{ "user", 2L }
			});

		migrationBuilder.CreateIndex(
			"ix_moderation_log_by_id",
			"moderation_log",
			"by_id");

		migrationBuilder.CreateIndex(
			"ix_moderation_log_target_id_timestamp",
			"moderation_log",
			new[] { "target_id", "timestamp" },
			descending: new[] { false, true });

		migrationBuilder.CreateIndex(
			"ix_relationships_target_id",
			"relationships",
			"target_id");

		migrationBuilder.CreateIndex(
			"ix_restrictions_user_id",
			"restrictions",
			"user_id");

		migrationBuilder.CreateIndex(
			"ix_users_active_name",
			"users",
			"active_name",
			unique: true);

		migrationBuilder.CreateIndex(
			"ix_users_normalized_name",
			"users",
			"normalized_name");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"moderation_log");

		migrationBuilder.DropTable(
			"relationships");

		migrationBuilder.DropTable(
			"restrictions");

		migrationBuilder.DropTable(
			"user_id_counters");

		migrationBuilder.DropTable(
			"users");
	}
}