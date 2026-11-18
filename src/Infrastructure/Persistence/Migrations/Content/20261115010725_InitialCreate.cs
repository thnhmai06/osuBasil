using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basil.Infrastructure.Persistence.Migrations.Content
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_banners",
                columns: table => new
                {
                    image = table.Column<string>(maxLength: 255, nullable: false),
                    url = table.Column<string>(nullable: false),
                    starts_at = table.Column<DateTimeOffset>(nullable: true),
                    ends_at = table.Column<DateTimeOffset>(nullable: true),
                    created_at = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_banners", x => x.image);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_banners");
        }
    }
}
