using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Auth.Migrations
{
    /// <inheritdoc />
    public partial class UniqueSessionTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_token_hash",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                table: "sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_token_hash",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                table: "sessions",
                column: "token_hash");
        }
    }
}
