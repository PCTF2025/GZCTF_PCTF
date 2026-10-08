using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 按周设置题目（时间窗口版）：每周独立的起止时间与名称、挑战题/其他题分组名，题目增加归属分桶
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261010120000_AddGameWeekWindows")]
public partial class AddGameWeekWindows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // WeekModeEnabled 由更早的迁移创建，此处不再重复添加

        // 旧版按「天数」记录周次时长的列已废弃，改为精确起止时间
        for (var stale = 1; stale <= 5; stale++)
        {
            migrationBuilder.DropColumn(
                name: $"Week{stale}DurationDays",
                table: "Games");
        }

        // 每周独立的起止时间窗口（null 表示该端不限制）
        for (var week = 1; week <= 5; week++)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: $"Week{week}StartUtc",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: $"Week{week}EndUtc",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: $"Week{week}Name",
                table: "Games",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        // 挑战题 / 其他题两个分组的自定义名称
        migrationBuilder.AddColumn<string>(
            name: "ChallengeBucketName",
            table: "Games",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MiscBucketName",
            table: "Games",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // 题目归属分桶：1-5 周 / 6 挑战题 / 7 其他题，null 未归类
        migrationBuilder.AddColumn<int>(
            name: "Week",
            table: "GameChallenges",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Week", table: "GameChallenges");

        migrationBuilder.DropColumn(name: "ChallengeBucketName", table: "Games");
        migrationBuilder.DropColumn(name: "MiscBucketName", table: "Games");

        for (var week = 1; week <= 5; week++)
        {
            migrationBuilder.DropColumn(name: $"Week{week}StartUtc", table: "Games");
            migrationBuilder.DropColumn(name: $"Week{week}EndUtc", table: "Games");
            migrationBuilder.DropColumn(name: $"Week{week}Name", table: "Games");
        }

        // WeekModeEnabled 保留给更早的迁移负责

        for (var stale = 1; stale <= 5; stale++)
        {
            migrationBuilder.AddColumn<int>(
                name: $"Week{stale}DurationDays",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 7);
        }
    }
}
