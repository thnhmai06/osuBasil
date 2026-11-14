#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Multiplayer;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"matches",
			table => new
			{
				id = table.Column<int>(nullable: false),
				name = table.Column<string>(nullable: false),
				creator_id = table.Column<int>(nullable: false),
				started_at = table.Column<DateTimeOffset>(nullable: false),
				ended_at = table.Column<DateTimeOffset>(nullable: true),
				is_private = table.Column<bool>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_matches", x => x.id);
				table.CheckConstraint("matches_id_check", "id >= 1");
				table.ForeignKey(
					"fk_matches_users_creator_id",
					x => x.creator_id,
					"users",
					"id");
			});

		migrationBuilder.CreateTable(
			"multiplayer_id_counters",
			table => new
			{
				name = table.Column<string>(maxLength: 64, nullable: false),
				next = table.Column<long>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_multiplayer_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"round_scores",
			table => new
			{
				score_id = table.Column<int>(nullable: false),
				match_id = table.Column<int>(nullable: false),
				round_number = table.Column<int>(nullable: false),
				team = table.Column<int>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_round_scores", x => x.score_id);
				table.ForeignKey(
					"fk_round_scores_scores_score_id",
					x => x.score_id,
					"scores",
					"id");
			});

		migrationBuilder.CreateTable(
			"match_events",
			table => new
			{
				id = table.Column<long>(nullable: false),
				match_id = table.Column<int>(nullable: false),
				type = table.Column<int>(nullable: false),
				timestamp = table.Column<DateTimeOffset>(nullable: false),
				actor_id = table.Column<int>(nullable: true),
				target_id = table.Column<int>(nullable: true),
				detail = table.Column<string>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_match_events", x => x.id);
				table.ForeignKey(
					"fk_match_events_matches_match_id",
					x => x.match_id,
					"matches",
					"id");
				table.ForeignKey(
					"fk_match_events_users_actor_id",
					x => x.actor_id,
					"users",
					"id");
				table.ForeignKey(
					"fk_match_events_users_target_id",
					x => x.target_id,
					"users",
					"id");
			});

		migrationBuilder.CreateTable(
			"rounds",
			table => new
			{
				match_id = table.Column<int>(nullable: false),
				number = table.Column<int>(nullable: false),
				beatmap_hash = table.Column<string>(nullable: false),
				mode = table.Column<int>(nullable: false),
				mods = table.Column<int>(nullable: false),
				freemods = table.Column<bool>(nullable: false),
				team_type = table.Column<int>(nullable: false),
				win_condition = table.Column<int>(nullable: false),
				seed = table.Column<int>(nullable: false),
				started_at = table.Column<DateTimeOffset>(nullable: false),
				ended_at = table.Column<DateTimeOffset>(nullable: true),
				aborted = table.Column<bool>(nullable: false),
				result = table.Column<string>(nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_rounds", x => new { x.match_id, x.number });
				table.ForeignKey(
					"fk_rounds_matches_match_id",
					x => x.match_id,
					"matches",
					"id");
			});

		migrationBuilder.InsertData(
			"multiplayer_id_counters",
			new[] { "name", "next" },
			new object[,]
			{
				{ "match", 1L },
				{ "match_event", 1L }
			});

		migrationBuilder.CreateIndex(
			"ix_match_events_actor_id",
			"match_events",
			"actor_id");

		migrationBuilder.CreateIndex(
			"ix_match_events_match_id",
			"match_events",
			"match_id");

		migrationBuilder.CreateIndex(
			"ix_match_events_target_id",
			"match_events",
			"target_id");

		migrationBuilder.CreateIndex(
			"ix_matches_creator_id",
			"matches",
			"creator_id");

		migrationBuilder.CreateIndex(
			"ix_matches_ended_at",
			"matches",
			"ended_at");

		migrationBuilder.CreateIndex(
			"ix_round_scores_match_id_round_number",
			"round_scores",
			new[] { "match_id", "round_number" });
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"match_events");

		migrationBuilder.DropTable(
			"multiplayer_id_counters");

		migrationBuilder.DropTable(
			"round_scores");

		migrationBuilder.DropTable(
			"rounds");

		migrationBuilder.DropTable(
			"matches");
	}
}