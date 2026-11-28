using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Content
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_banners",
                columns: table => new
                {
                    image = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_banners", x => x.image);
                });

            migrationBuilder.InsertData(
                table: "menu_banners",
                columns: new[] { "image", "created_at", "ends_at", "starts_at", "url" },
                values: new object[] { "Menu/Banners/default-banner@2x.png", new DateTimeOffset(new DateTime(2026, 11, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "https://github.com/thnhmai06/osuBasil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_banners");
        }
    }
}
