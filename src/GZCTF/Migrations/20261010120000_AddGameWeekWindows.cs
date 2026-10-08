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
        // 兼容两种历史：
        //  - 已执行过 20261008120000_AddGameWeekSettings 的库（本地增量升级）
        //  - 全新数据库（列不存在，直接建）
        // 因此所有“废弃列”的删除都必须是幂等的。
        migrationBuilder.Sql(@"
DO $$
BEGIN
    -- 旧版按天数记录的周次时长列，存在才删
    FOR i IN 1..5 LOOP
        EXECUTE format('ALTER TABLE ""Games"" DROP COLUMN IF EXISTS ""Week%1$sDurationDays""', i);
    END LOOP;

    -- 周次模式开关：仅在缺失时补建
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Games' AND column_name = 'WeekModeEnabled'
    ) THEN
        ALTER TABLE ""Games"" ADD COLUMN ""WeekModeEnabled"" boolean NOT NULL DEFAULT false;
    END IF;
END $$;
");

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

        // 回滚时同样保持幂等：只删本次新增的列，不碰 WeekModeEnabled
        // （WeekModeEnabled 的归属由更早的迁移决定）
        for (var week = 1; week <= 5; week++)
        {
            migrationBuilder.DropColumn(name: $"Week{week}StartUtc", table: "Games");
            migrationBuilder.DropColumn(name: $"Week{week}EndUtc", table: "Games");
            migrationBuilder.DropColumn(name: $"Week{week}Name", table: "Games");
        }

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
