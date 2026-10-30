using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 题目新增「外部动态 flag」开关。
///
/// 用于共用一台常开服务器的题目（如 Minecraft）：不下发容器，
/// 但 flag 由平台按队伍确定性生成，选手通过队伍 Token 向外部服务兑换。
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261030010000_AddExternalDynamicFlag")]
public partial class AddExternalDynamicFlag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""GameChallenges""
    ADD COLUMN IF NOT EXISTS ""ExternalDynamicFlag"" boolean NOT NULL DEFAULT false;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""GameChallenges"" DROP COLUMN IF EXISTS ""ExternalDynamicFlag"";
");
    }
}
