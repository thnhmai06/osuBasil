using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Basil.Infrastructure.Persistence.Migrations.Users
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_id_counters",
                columns: table => new
                {
                    name = table.Column<string>(maxLength: 64, nullable: false),
                    next = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_id_counters", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false),
                    name = table.Column<string>(nullable: false),
                    normalized_name = table.Column<string>(maxLength: 255, nullable: false),
                    active_name = table.Column<string>(maxLength: 255, nullable: true),
                    country = table.Column<int>(nullable: false),
                    permissions = table.Column<long>(nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("users_id_check", "id >= 1");
                });

            migrationBuilder.CreateTable(
                name: "moderation_log",
                columns: table => new
                {
                    id = table.Column<long>(nullable: false),
                    action = table.Column<int>(nullable: false),
                    by_id = table.Column<int>(nullable: false),
                    target_id = table.Column<int>(nullable: false),
                    reason = table.Column<string>(nullable: true),
                    timestamp = table.Column<DateTimeOffset>(nullable: false)
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
                    actor_id = table.Column<int>(nullable: false),
                    target_id = table.Column<int>(nullable: false),
                    type = table.Column<int>(nullable: false),
                    created_at = table.Column<DateTimeOffset>(nullable: false)
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
                    id = table.Column<int>(nullable: false),
                    user_id = table.Column<int>(nullable: false),
                    permissions = table.Column<long>(nullable: false),
                    starts_at = table.Column<DateTimeOffset>(nullable: false),
                    ends_at = table.Column<DateTimeOffset>(nullable: true)
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

            migrationBuilder.InsertData(
                table: "user_id_counters",
                columns: new[] { "name", "next" },
                values: new object[,]
                {
                    { "moderation_log", 1L },
                    { "restriction", 1L },
                    { "user", 2L }
                });

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
                name: "ix_users_active_name",
                table: "users",
                column: "active_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_name",
                table: "users",
                column: "normalized_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_log");

            migrationBuilder.DropTable(
                name: "relationships");

            migrationBuilder.DropTable(
                name: "restrictions");

            migrationBuilder.DropTable(
                name: "user_id_counters");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
