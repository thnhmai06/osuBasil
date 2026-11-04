using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Postgres.Migrations.Scores
{
    /// <inheritdoc />
    public partial class ScoreLeaderboardIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scores_beatmap_hash",
                table: "scores");

            migrationBuilder.CreateIndex(
                name: "ix_scores_beatmap_hash_total_score_timestamp_id",
                table: "scores",
                columns: new[] { "beatmap_hash", "total_score", "timestamp", "id" },
                descending: new[] { false, true, false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scores_beatmap_hash_total_score_timestamp_id",
                table: "scores");

            migrationBuilder.CreateIndex(
                name: "ix_scores_beatmap_hash",
                table: "scores",
                column: "beatmap_hash");
        }
    }
}
