using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissionsAndChallengeAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 使用幂等 SQL：多台实例可能并发启动，重复执行不应报错
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'GameChallenges' AND column_name = 'AuthorId') THEN
                        ALTER TABLE "GameChallenges" ADD COLUMN "AuthorId" uuid NULL;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'ExerciseChallenges' AND column_name = 'AuthorId') THEN
                        ALTER TABLE "ExerciseChallenges" ADD COLUMN "AuthorId" uuid NULL;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'AspNetUsers' AND column_name = 'Permissions') THEN
                        ALTER TABLE "AspNetUsers" ADD COLUMN "Permissions" integer NOT NULL DEFAULT 0;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'AspNetUsers' AND column_name = 'ManagedCategories') THEN
                        ALTER TABLE "AspNetUsers" ADD COLUMN "ManagedCategories" character varying(128) NOT NULL DEFAULT '';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS "GameChallenges" DROP COLUMN IF EXISTS "AuthorId";
                ALTER TABLE IF EXISTS "ExerciseChallenges" DROP COLUMN IF EXISTS "AuthorId";
                ALTER TABLE IF EXISTS "AspNetUsers" DROP COLUMN IF EXISTS "ManagedCategories";
                ALTER TABLE IF EXISTS "AspNetUsers" DROP COLUMN IF EXISTS "Permissions";
                """);
        }
    }
}
