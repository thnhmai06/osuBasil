using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Beatmaps
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "beatmap_id_counters",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beatmap_id_counters", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "beatmapsets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    artist = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    creator = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archive_hash = table.Column<string>(type: "text", nullable: false),
                    archive_size = table.Column<long>(type: "bigint", nullable: false),
                    archive_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    storyboard = table.Column<bool>(type: "boolean", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beatmapsets", x => x.id);
                    table.CheckConstraint("beatmapsets_id_check", "id >= 1");
                });

            migrationBuilder.CreateTable(
                name: "beatmaps",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    beatmapset_id = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    star = table.Column<double>(type: "double precision", nullable: true),
                    length = table.Column<int>(type: "integer", nullable: true),
                    bpm = table.Column<double>(type: "double precision", nullable: true),
                    cs = table.Column<double>(type: "double precision", nullable: true),
                    ar = table.Column<double>(type: "double precision", nullable: true),
                    od = table.Column<double>(type: "double precision", nullable: true),
                    hp = table.Column<double>(type: "double precision", nullable: true),
                    object_count = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    circles = table.Column<int>(type: "integer", nullable: true),
                    sliders = table.Column<int>(type: "integer", nullable: true),
                    background = table.Column<string>(type: "text", nullable: true),
                    audio = table.Column<string>(type: "text", nullable: true),
                    video = table.Column<string>(type: "text", nullable: true),
                    preview_time = table.Column<int>(type: "integer", nullable: true),
                    storyboard = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beatmaps", x => x.id);
                    table.CheckConstraint("beatmaps_id_check", "id >= 1");
                    table.ForeignKey(
                        name: "fk_beatmaps_beatmapsets_beatmapset_id",
                        column: x => x.beatmapset_id,
                        principalTable: "beatmapsets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "beatmap_id_counters",
                columns: new[] { "name", "next" },
                values: new object[,]
                {
                    { "local_beatmap", 1000000000L },
                    { "local_beatmapset", 1000000000L }
                });

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_beatmapset_id",
                table: "beatmaps",
                column: "beatmapset_id");

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_file_name",
                table: "beatmaps",
                column: "file_name");

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_hash",
                table: "beatmaps",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_mode",
                table: "beatmaps",
                column: "mode");

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_version",
                table: "beatmaps",
                column: "version")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_beatmapsets_artist",
                table: "beatmapsets",
                column: "artist")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_beatmapsets_creator",
                table: "beatmapsets",
                column: "creator")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_beatmapsets_title",
                table: "beatmapsets",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beatmap_id_counters");

            migrationBuilder.DropTable(
                name: "beatmaps");

            migrationBuilder.DropTable(
                name: "beatmapsets");
        }
    }
}
