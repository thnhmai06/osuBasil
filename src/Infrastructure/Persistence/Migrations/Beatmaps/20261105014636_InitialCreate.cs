#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Beatmaps;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
	/// <inheritdoc />
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			"beatmap_id_counters",
			table => new
			{
				name = table.Column<string>(maxLength: 64, nullable: false),
				next = table.Column<long>(nullable: false)
			},
			constraints: table => { table.PrimaryKey("pk_beatmap_id_counters", x => x.name); });

		migrationBuilder.CreateTable(
			"beatmapsets",
			table => new
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
				status = table.Column<int>(nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("pk_beatmapsets", x => x.id);
				table.CheckConstraint("beatmapsets_id_check", "id >= 1");
			});

		migrationBuilder.CreateTable(
			"beatmaps",
			table => new
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
					"fk_beatmaps_beatmapsets_beatmapset_id",
					x => x.beatmapset_id,
					"beatmapsets",
					"id",
					onDelete: ReferentialAction.Cascade);
			});

		migrationBuilder.InsertData(
			"beatmap_id_counters",
			new[] { "name", "next" },
			new object[,]
			{
				{ "local_beatmap", 1000000000L },
				{ "local_beatmapset", 1000000000L }
			});

		migrationBuilder.CreateIndex(
			"ix_beatmaps_beatmapset_id",
			"beatmaps",
			"beatmapset_id");

		migrationBuilder.CreateIndex(
			"ix_beatmaps_hash",
			"beatmaps",
			"hash",
			unique: true);

		migrationBuilder.CreateIndex(
			"ix_beatmaps_mode",
			"beatmaps",
			"mode");
	}

	/// <inheritdoc />
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(
			"beatmap_id_counters");

		migrationBuilder.DropTable(
			"beatmaps");

		migrationBuilder.DropTable(
			"beatmapsets");
	}
}