using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Multiplayer;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "matches",
			columns: table => new
			{
				id = table.Column<int>(type: "integer", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
				name = table.Column<string>(type: "text", nullable: false),
				creator_id = table.Column<int>(type: "integer", nullable: true),
				started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				is_private = table.Column<bool>(type: "boolean", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_matches", x => x.id);
				table.CheckConstraint("matches_id_check", "id >= 1");
				table.ForeignKey(
					name: "fk_matches_users_creator_id",
					column: x => x.creator_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "round_scores",
			columns: table => new
			{
				score_id = table.Column<int>(type: "integer", nullable: false),
				match_id = table.Column<int>(type: "integer", nullable: false),
				round_number = table.Column<int>(type: "integer", nullable: false),
				team = table.Column<int>(type: "integer", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_round_scores", x => x.score_id);
				table.ForeignKey(
					name: "fk_round_scores_scores_score_id",
					column: x => x.score_id,
					principalTable: "scores",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "match_events",
			columns: table => new
			{
				id = table.Column<long>(type: "bigint", nullable: false)
					.Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
				match_id = table.Column<int>(type: "integer", nullable: false),
				type = table.Column<int>(type: "integer", nullable: false),
				timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				actor_id = table.Column<int>(type: "integer", nullable: true),
				target_id = table.Column<int>(type: "integer", nullable: true),
				detail = table.Column<string>(type: "text", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_match_events", x => x.id);
				table.ForeignKey(
					name: "fk_match_events_matches_match_id",
					column: x => x.match_id,
					principalTable: "matches",
					principalColumn: "id");
				table.ForeignKey(
					name: "fk_match_events_users_actor_id",
					column: x => x.actor_id,
					principalTable: "users",
					principalColumn: "id");
				table.ForeignKey(
					name: "fk_match_events_users_target_id",
					column: x => x.target_id,
					principalTable: "users",
					principalColumn: "id");
			});

		migrationBuilder.CreateTable(
			name: "rounds",
			columns: table => new
			{
				match_id = table.Column<int>(type: "integer", nullable: false),
				number = table.Column<int>(type: "integer", nullable: false),
				beatmap_hash = table.Column<string>(type: "text", nullable: false),
				mode = table.Column<int>(type: "integer", nullable: false),
				mods = table.Column<int>(type: "integer", nullable: false),
				freemods = table.Column<bool>(type: "boolean", nullable: false),
				team_type = table.Column<int>(type: "integer", nullable: false),
				win_condition = table.Column<int>(type: "integer", nullable: false),
				seed = table.Column<int>(type: "integer", nullable: false),
				started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
				ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
				aborted = table.Column<bool>(type: "boolean", nullable: false),
				result = table.Column<string>(type: "jsonb", nullable: true)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_rounds", x => new { x.match_id, x.number });
				table.ForeignKey(
					name: "fk_rounds_matches_match_id",
					column: x => x.match_id,
					principalTable: "matches",
					principalColumn: "id");
			});

		migrationBuilder.CreateIndex(
			name: "ix_match_events_actor_id",
			table: "match_events",
			column: "actor_id");

		migrationBuilder.CreateIndex(
			name: "ix_match_events_match_id",
			table: "match_events",
			column: "match_id");

		migrationBuilder.CreateIndex(
			name: "ix_match_events_target_id",
			table: "match_events",
			column: "target_id");

		migrationBuilder.CreateIndex(
			name: "ix_matches_creator_id",
			table: "matches",
			column: "creator_id");

		migrationBuilder.CreateIndex(
			name: "ix_matches_ended_at",
			table: "matches",
			column: "ended_at");

		migrationBuilder.CreateIndex(
			name: "ix_round_scores_match_id_round_number",
			table: "round_scores",
			columns: ["match_id", "round_number"]);
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			name: "match_events");

		migrationBuilder.DropTable(
			name: "round_scores");

		migrationBuilder.DropTable(
			name: "rounds");

		migrationBuilder.DropTable(
			name: "matches");
	}
}