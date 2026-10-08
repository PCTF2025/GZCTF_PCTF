using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 按周设置题目：比赛增加周次模式开关与每周时长，题目增加所属周次
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261008120000_AddGameWeekSettings")]
public partial class AddGameWeekSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "WeekModeEnabled",
            table: "Games",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "Week1DurationDays",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 7);

        migrationBuilder.AddColumn<int>(
            name: "Week2DurationDays",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 7);

        migrationBuilder.AddColumn<int>(
            name: "Week3DurationDays",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 7);

        migrationBuilder.AddColumn<int>(
            name: "Week4DurationDays",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 7);

        migrationBuilder.AddColumn<int>(
            name: "Week5DurationDays",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 7);

        migrationBuilder.AddColumn<int>(
            name: "Week",
            table: "GameChallenges",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WeekModeEnabled", table: "Games");
        migrationBuilder.DropColumn(name: "Week1DurationDays", table: "Games");
        migrationBuilder.DropColumn(name: "Week2DurationDays", table: "Games");
        migrationBuilder.DropColumn(name: "Week3DurationDays", table: "Games");
        migrationBuilder.DropColumn(name: "Week4DurationDays", table: "Games");
        migrationBuilder.DropColumn(name: "Week5DurationDays", table: "Games");
        migrationBuilder.DropColumn(name: "Week", table: "GameChallenges");
    }
}
