using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Basil.Infrastructure.Scores.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "scores",
			columns: table => new
			{
				id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				user_id = table.Column<int>(type: "integer", nullable: true),
				beatmap_hash = table.Column<string>(type: "text", nullable: true),
				mode = table.Column<int>(type: "integer", nullable: false),
				mods = table.Column<int>(type: "integer", nullable: false),
				num300 = table.Column<int>(type: "integer", nullable: false),
				num100 = table.Column<int>(type: "integer", nullable: false),
				num50 = table.Column<int>(type: "integer", nullable: false),
				num_geki = table.Column<int>(type: "integer", nullable: false),
				num_katu = table.Column<int>(type: "integer", nullable: false),
				num_miss = table.Column<int>(type: "integer", nullable: false),
				total_score = table.Column<int>(type: "integer", nullable: false),
				max_combo = table.Column<int>(type: "integer", nullable: false),
				current_combo = table.Column<int>(type: "integer", nullable: false),
				grade = table.Column<int>(type: "integer", nullable: false),
				is_passed = table.Column<bool>(type: "boolean", nullable: false),
				is_full_combo = table.Column<bool>(type: "boolean", nullable: false),
				timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				checksum = table.Column<string>(type: "text", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_scores", x => x.id);
				table.CheckConstraint("scores_id_check", "id >= 1");
				table.ForeignKey(
					name: "fk_scores_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "user_stats",
			columns: table => new
			{
				user_id = table.Column<int>(type: "integer", nullable: false),
				mode = table.Column<int>(type: "integer", nullable: false),
				total_score = table.Column<long>(type: "bigint", nullable: false),
				ranked_score = table.Column<long>(type: "bigint", nullable: false),
				play_count = table.Column<int>(type: "integer", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_user_stats", x => new { x.user_id, x.mode });
				table.ForeignKey(
					name: "fk_user_stats_users_user_id",
					column: x => x.user_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateIndex(
			name: "ix_scores_beatmap_hash",
			table: "scores",
			column: "beatmap_hash");

		migrationBuilder.CreateIndex(
			name: "ix_scores_checksum",
			table: "scores",
			column: "checksum",
			unique: true);

		migrationBuilder.CreateIndex(
			name: "ix_scores_user_id",
			table: "scores",
			column: "user_id");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "scores");

		migrationBuilder.DropTable(
			name: "user_stats");
	}
}