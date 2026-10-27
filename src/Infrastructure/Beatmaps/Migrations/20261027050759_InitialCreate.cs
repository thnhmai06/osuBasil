using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Beatmaps.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateSequence<int>(
			name: "local_beatmap_ids",
			startValue: 1000000000L);

		migrationBuilder.CreateSequence<int>(
			name: "local_beatmapset_ids",
			startValue: 1000000000L);

		migrationBuilder.CreateTable(
			name: "beatmapsets",
			columns: table => new
			{
				id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('local_beatmapset_ids')"),
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
				visible = table.Column<bool>(type: "boolean", nullable: false)
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
				id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('local_beatmap_ids')"),
				beatmapset_id = table.Column<int>(type: "integer", nullable: false),
				hash = table.Column<string>(type: "text", nullable: false),
				version = table.Column<string>(type: "text", nullable: false),
				mode = table.Column<int>(type: "integer", nullable: false),
				star = table.Column<double>(type: "double precision", nullable: false),
				length = table.Column<int>(type: "integer", nullable: false),
				bpm = table.Column<double>(type: "double precision", nullable: false),
				cs = table.Column<double>(type: "double precision", nullable: false),
				ar = table.Column<double>(type: "double precision", nullable: false),
				od = table.Column<double>(type: "double precision", nullable: false),
				hp = table.Column<double>(type: "double precision", nullable: false),
				objects = table.Column<JsonElement>(type: "jsonb", nullable: false),
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
			name: "beatmaps");

		migrationBuilder.DropTable(
			name: "beatmapsets");

		migrationBuilder.DropSequence(
			name: "local_beatmap_ids");

		migrationBuilder.DropSequence(
			name: "local_beatmapset_ids");
	}
}