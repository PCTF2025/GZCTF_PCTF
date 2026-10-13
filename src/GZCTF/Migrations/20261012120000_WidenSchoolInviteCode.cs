using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 放宽学校报名邀请码长度：32 → 64。
///
/// 学校邀请码允许直接粘贴标准 UUID（36 位含连字符），
/// 而 InviteTokenLength = 32 是 Guid "N" 格式（32 位无连字符）的长度。
/// 若不放宽，提交带连字符的 UUID 会触发模型校验错误：
///   "The field InviteCode must be a string or array type with a maximum length of '32'."
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261012120000_WidenSchoolInviteCode")]
public partial class WidenSchoolInviteCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 幂等：仅当当前长度不足 64 时才扩宽。
        // 使用 Sql 而非 AlterColumn，避免历史库列名/类型差异导致失败。
        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Schools'
          AND column_name = 'InviteCode'
          AND character_maximum_length IS NOT NULL
          AND character_maximum_length < 64
    ) THEN
        ALTER TABLE ""Schools"" ALTER COLUMN ""InviteCode"" TYPE character varying(64);
    END IF;
END $$;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 回滚前先把超长邀请码截断，否则窄化列会失败
        migrationBuilder.Sql(@"
UPDATE ""Schools""
SET ""InviteCode"" = LEFT(""InviteCode"", 32)
WHERE ""InviteCode"" IS NOT NULL AND LENGTH(""InviteCode"") > 32;
");

        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Schools'
          AND column_name = 'InviteCode'
          AND character_maximum_length = 64
    ) THEN
        ALTER TABLE ""Schools"" ALTER COLUMN ""InviteCode"" TYPE character varying(32);
    END IF;
END $$;
");
    }
}
