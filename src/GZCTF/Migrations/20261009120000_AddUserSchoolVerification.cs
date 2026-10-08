using GZCTF.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations;

/// <summary>
/// 学籍信息审核：新增年级、审核状态、审核备注、审核人、审核时间，
/// 以及学校管理员负责的学校列表（ManagedSchools）。
/// 主办赛道报名要求学籍信息经学校管理员审核通过。
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261009120000_AddUserSchoolVerification")]
public partial class AddUserSchoolVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 幂等：全新库与增量升级库都适用，避免迁移链断裂导致启动失败
        migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'Grade') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""Grade"" character varying(128) NOT NULL DEFAULT '';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'VerifyStatus') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""VerifyStatus"" smallint NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'VerifyNote') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""VerifyNote"" character varying(128) NOT NULL DEFAULT '';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'VerifiedById') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""VerifiedById"" uuid NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'VerifiedAtUtc') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""VerifiedAtUtc"" timestamp with time zone NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AspNetUsers' AND column_name = 'ManagedSchools') THEN
        ALTER TABLE ""AspNetUsers"" ADD COLUMN ""ManagedSchools"" character varying(128) NOT NULL DEFAULT '';
    END IF;
END $$;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""Grade"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""VerifyStatus"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""VerifyNote"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""VerifiedById"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""VerifiedAtUtc"";
ALTER TABLE ""AspNetUsers"" DROP COLUMN IF EXISTS ""ManagedSchools"";
");
    }
}
