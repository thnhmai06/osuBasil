#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Basil.Infrastructure.Persistence.Migrations.Beatmaps;

public partial class NullableDifficulty : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AlterColumn<double>(
			name: "star",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));

		migrationBuilder.AlterColumn<double>(
			name: "od",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));

		migrationBuilder.AlterColumn<int>(
			name: "length",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(int));

		migrationBuilder.AlterColumn<double>(
			name: "hp",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));

		migrationBuilder.AlterColumn<double>(
			name: "cs",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));

		migrationBuilder.AlterColumn<double>(
			name: "bpm",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));

		migrationBuilder.AlterColumn<double>(
			name: "ar",
			table: "beatmaps",
			nullable: true,
			oldClrType: typeof(double));
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AlterColumn<double>(
			name: "star",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);

		migrationBuilder.AlterColumn<double>(
			name: "od",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);

		migrationBuilder.AlterColumn<int>(
			name: "length",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0,
			oldClrType: typeof(int),
			oldNullable: true);

		migrationBuilder.AlterColumn<double>(
			name: "hp",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);

		migrationBuilder.AlterColumn<double>(
			name: "cs",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);

		migrationBuilder.AlterColumn<double>(
			name: "bpm",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);

		migrationBuilder.AlterColumn<double>(
			name: "ar",
			table: "beatmaps",
			nullable: false,
			defaultValue: 0.0,
			oldClrType: typeof(double),
			oldNullable: true);
	}
}