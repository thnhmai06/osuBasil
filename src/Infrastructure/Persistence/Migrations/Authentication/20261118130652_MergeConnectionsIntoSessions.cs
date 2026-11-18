using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Authentication
{
    /// <inheritdoc />
    public partial class MergeConnectionsIntoSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connection_id_counters");

            migrationBuilder.DropTable(
                name: "connections");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "connection_id_counters",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connection_id_counters", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "connections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    client_info = table.Column<string>(type: "text", nullable: true),
                    close_reason = table.Column<byte>(type: "smallint", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_connections_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_connections_closed_at",
                table: "connections",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ix_connections_session_id_opened_at",
                table: "connections",
                columns: new[] { "session_id", "opened_at" },
                descending: new[] { false, true });
        }
    }
}
