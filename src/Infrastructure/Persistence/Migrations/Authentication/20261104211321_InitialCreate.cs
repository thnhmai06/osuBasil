using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Authentication
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    user_id = table.Column<int>(nullable: false),
                    password_hash = table.Column<string>(nullable: false)
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
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<int>(nullable: false),
                    type = table.Column<byte>(nullable: false),
                    ip = table.Column<string>(maxLength: 45, nullable: false),
                    client_info = table.Column<string>(nullable: true),
                    started_at = table.Column<DateTimeOffset>(nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(nullable: false),
                    ended_at = table.Column<DateTimeOffset>(nullable: true),
                    end_reason = table.Column<byte>(nullable: true),
                    token_hash = table.Column<byte[]>(maxLength: 64, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(nullable: true),
                    client_date = table.Column<string>(nullable: true),
                    client_revision = table.Column<int>(nullable: true),
                    client_stream = table.Column<int>(nullable: true),
                    osu_path_hash = table.Column<string>(nullable: true),
                    network_adapters = table.Column<string>(nullable: true),
                    network_adapters_hash = table.Column<string>(nullable: true),
                    uninstall_hash = table.Column<string>(nullable: true),
                    disk_signature_hash = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_started_at",
                table: "sessions",
                columns: new[] { "user_id", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "sessions");
        }
    }
}
