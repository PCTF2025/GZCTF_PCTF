using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class SeedSocialSchool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 「社会」归属：注册时未填邀请码或邀请码无效的学生归入该组。
            // Enabled 为 false —— 不出现在登录页与绑定列表，仅作归属标识。
            // 幂等：以短名 social 判重。
            migrationBuilder.Sql("""
                INSERT INTO "Schools"
                    ("Name", "Slug", "AuthType", "CasLoginUrl", "CasValidateUrl",
                     "EmailDomains", "IconUrl", "InviteCode", "Enabled", "Order",
                     "FreshmanGrades", "SeniorGrades")
                SELECT
                    '社会', 'social', 0, NULL, NULL,
                    '', NULL, NULL, false, 9999,
                    '', ''
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Schools" WHERE "Slug" = 'social'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 不删除该条目：可能已有用户归属于此，删除会产生孤儿归属。
        }
    }
}
