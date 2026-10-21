using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations
{
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
                name: "channels",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    read_privileges = table.Column<int>(type: "integer", nullable: false),
                    write_privileges = table.Column<int>(type: "integer", nullable: false),
                    auto_join = table.Column<bool>(type: "boolean", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "menu_banners",
                columns: table => new
                {
                    image = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_banners", x => x.image);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<int>(type: "integer", nullable: false),
                    permissions = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("users_id_check", "id >= 1");
                });

            migrationBuilder.CreateTable(
                name: "write_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_write_tickets", x => x.id);
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
                    objects = table.Column<string>(type: "jsonb", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "logins",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_date = table.Column<string>(type: "text", nullable: true),
                    client_revision = table.Column<int>(type: "integer", nullable: true),
                    client_stream = table.Column<int>(type: "integer", nullable: true),
                    osu_path_hash = table.Column<string>(type: "text", nullable: true),
                    network_adapters = table.Column<string>(type: "text", nullable: true),
                    network_adapters_hash = table.Column<string>(type: "text", nullable: true),
                    uninstall_hash = table.Column<string>(type: "text", nullable: true),
                    disk_signature_hash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logins", x => x.id);
                    table.ForeignKey(
                        name: "fk_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "moderation_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    action = table.Column<int>(type: "integer", nullable: false),
                    by_id = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_log_users_by_id",
                        column: x => x.by_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_moderation_log_users_target_id",
                        column: x => x.target_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "relationships",
                columns: table => new
                {
                    actor_id = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relationships", x => new { x.actor_id, x.target_id });
                    table.ForeignKey(
                        name: "fk_relationships_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_relationships_users_target_id",
                        column: x => x.target_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "restrictions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    permissions = table.Column<long>(type: "bigint", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restrictions", x => x.id);
                    table.CheckConstraint("restrictions_id_check", "id >= 1");
                    table.ForeignKey(
                        name: "fk_restrictions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    aborted = table.Column<bool>(type: "boolean", nullable: false)
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

            migrationBuilder.InsertData(
                table: "channels",
                columns: new[] { "name", "auto_join", "read_privileges", "topic", "visible", "write_privileges" },
                values: new object[,]
                {
                    { "#lobby", false, 0, "Multiplayer lobby discussion.", true, 0 },
                    { "#osu", true, 0, "General discussion.", true, 0 }
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

            migrationBuilder.CreateIndex(
                name: "ix_logins_user_id_timestamp",
                table: "logins",
                columns: new[] { "user_id", "timestamp" },
                descending: new[] { false, true });

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
                name: "ix_moderation_log_by_id",
                table: "moderation_log",
                column: "by_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_log_target_id_timestamp",
                table: "moderation_log",
                columns: new[] { "target_id", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_relationships_target_id",
                table: "relationships",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_restrictions_user_id",
                table: "restrictions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_round_scores_match_id_round_number",
                table: "round_scores",
                columns: new[] { "match_id", "round_number" });

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

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_name",
                table: "users",
                column: "normalized_name",
                unique: true,
                filter: "deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_write_tickets_created_at",
                table: "write_tickets",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beatmaps");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "logins");

            migrationBuilder.DropTable(
                name: "match_events");

            migrationBuilder.DropTable(
                name: "menu_banners");

            migrationBuilder.DropTable(
                name: "moderation_log");

            migrationBuilder.DropTable(
                name: "relationships");

            migrationBuilder.DropTable(
                name: "restrictions");

            migrationBuilder.DropTable(
                name: "round_scores");

            migrationBuilder.DropTable(
                name: "rounds");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "user_stats");

            migrationBuilder.DropTable(
                name: "write_tickets");

            migrationBuilder.DropTable(
                name: "beatmapsets");

            migrationBuilder.DropTable(
                name: "scores");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropSequence(
                name: "local_beatmap_ids");

            migrationBuilder.DropSequence(
                name: "local_beatmapset_ids");
        }
    }
}
