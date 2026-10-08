using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 用户学校绑定：新增 School（学校短名）与 SchoolSource（绑定来源）两列。
/// 用于主办赛道报名的资格校验：快速登录自动绑定，或使用邀请码手动绑定。
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261008130000_AddUserSchoolBinding")]
public partial class AddUserSchoolBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 保持幂等：全新库与增量升级库都适用，避免迁移链断裂导致启动失败
        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'School'
    ) THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""School"" character varying(128) NOT NULL DEFAULT '';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'SchoolSource'
    ) THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""SchoolSource"" smallint NOT NULL DEFAULT 0;
    END IF;
END $$;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""School"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""SchoolSource"";
");
    }
}
