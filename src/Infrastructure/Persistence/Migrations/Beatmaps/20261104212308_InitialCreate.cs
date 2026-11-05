using System;
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
            migrationBuilder.CreateTable(
                name: "beatmap_id_counters",
                columns: table => new
                {
                    name = table.Column<string>(maxLength: 64, nullable: false),
                    next = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_beatmap_id_counters", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "beatmapsets",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false),
                    artist = table.Column<string>(nullable: false),
                    title = table.Column<string>(nullable: false),
                    creator = table.Column<string>(nullable: false),
                    created_at = table.Column<DateTimeOffset>(nullable: false),
                    updated_at = table.Column<DateTimeOffset>(nullable: false),
                    archive_hash = table.Column<string>(nullable: false),
                    archive_size = table.Column<long>(nullable: false),
                    archive_modified_at = table.Column<DateTimeOffset>(nullable: false),
                    storyboard = table.Column<bool>(nullable: false),
                    locked = table.Column<bool>(nullable: false),
                    visible = table.Column<bool>(nullable: false),
                    status = table.Column<short>(nullable: false)
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
                    id = table.Column<int>(nullable: false),
                    beatmapset_id = table.Column<int>(nullable: false),
                    hash = table.Column<string>(maxLength: 128, nullable: false),
                    version = table.Column<string>(nullable: false),
                    mode = table.Column<int>(nullable: false),
                    star = table.Column<double>(nullable: false),
                    length = table.Column<int>(nullable: false),
                    bpm = table.Column<double>(nullable: false),
                    cs = table.Column<double>(nullable: false),
                    ar = table.Column<double>(nullable: false),
                    od = table.Column<double>(nullable: false),
                    hp = table.Column<double>(nullable: false),
                    objects = table.Column<string>(nullable: false),
                    circles = table.Column<int>(nullable: true),
                    sliders = table.Column<int>(nullable: true),
                    background = table.Column<string>(nullable: true),
                    audio = table.Column<string>(nullable: true),
                    video = table.Column<string>(nullable: true),
                    preview_time = table.Column<int>(nullable: true),
                    storyboard = table.Column<bool>(nullable: false),
                    created_at = table.Column<DateTimeOffset>(nullable: false),
                    updated_at = table.Column<DateTimeOffset>(nullable: false),
                    locked = table.Column<bool>(nullable: false),
                    visible = table.Column<bool>(nullable: false)
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
                name: "ix_beatmaps_hash",
                table: "beatmaps",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_beatmaps_mode",
                table: "beatmaps",
                column: "mode");
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
