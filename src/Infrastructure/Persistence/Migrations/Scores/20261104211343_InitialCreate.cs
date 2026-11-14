#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Scores;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"score_id_counters",
			table => new
			{
				name = table.Column<string>(maxLength: 64, nullable: false),
				next = table.Column<long>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_score_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"scores",
			table => new
			{
				id = table.Column<int>(nullable: false),
				user_id = table.Column<int>(nullable: true),
				beatmap_hash = table.Column<string>(maxLength: 128, nullable: true),
				mode = table.Column<int>(nullable: false),
				mods = table.Column<int>(nullable: false),
				num300 = table.Column<int>(nullable: false),
				num100 = table.Column<int>(nullable: false),
				num50 = table.Column<int>(nullable: false),
				num_geki = table.Column<int>(nullable: false),
				num_katu = table.Column<int>(nullable: false),
				num_miss = table.Column<int>(nullable: false),
				total_score = table.Column<int>(nullable: false),
				max_combo = table.Column<int>(nullable: false),
				current_combo = table.Column<int>(nullable: false),
				grade = table.Column<int>(nullable: false),
				is_passed = table.Column<bool>(nullable: false),
				is_full_combo = table.Column<bool>(nullable: false),
				timestamp = table.Column<DateTimeOffset>(nullable: false),
				checksum = table.Column<string>(maxLength: 128, nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_scores", x => x.id);
				table.CheckConstraint("scores_id_check", "id >= 1");
				table.ForeignKey(
					"fk_scores_users_user_id",
					x => x.user_id,
					"users",
					"id");
			});

		migrationBuilder.CreateTable(
			"user_stats",
			table => new
			{
				user_id = table.Column<int>(nullable: false),
				mode = table.Column<int>(nullable: false),
				total_score = table.Column<long>(nullable: false),
				ranked_score = table.Column<long>(nullable: false),
				play_count = table.Column<int>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_user_stats", x => new { x.user_id, x.mode });
				table.ForeignKey(
					"fk_user_stats_users_user_id",
					x => x.user_id,
					"users",
					"id");
			});

		migrationBuilder.InsertData(
			"score_id_counters",
			new[] { "name", "next" },
			new object[] { "score", 1L });

		migrationBuilder.CreateIndex(
			"ix_scores_beatmap_hash_total_score_timestamp_id",
			"scores",
			new[] { "beatmap_hash", "total_score", "timestamp", "id" },
			descending: new[] { false, true, false, false });

		migrationBuilder.CreateIndex(
			"ix_scores_checksum",
			"scores",
			"checksum",
			unique: true);

		migrationBuilder.CreateIndex(
			"ix_scores_user_id",
			"scores",
			"user_id");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"score_id_counters");

		migrationBuilder.DropTable(
			"scores");

		migrationBuilder.DropTable(
			"user_stats");
	}
}