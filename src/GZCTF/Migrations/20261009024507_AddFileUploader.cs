using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddFileUploader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 幂等：使用原生 SQL 判断列/索引/外键是否已存在，
            // 避免在手工建过列的库上重复执行失败
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_name = 'Files' AND column_name = 'UploadedById') THEN
                        ALTER TABLE "Files" ADD COLUMN "UploadedById" uuid NULL;
                    END IF;

                    IF NOT EXISTS (SELECT 1 FROM pg_indexes
                                   WHERE tablename = 'Files' AND indexname = 'IX_Files_UploadedById') THEN
                        CREATE INDEX "IX_Files_UploadedById" ON "Files" ("UploadedById");
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Files_AspNetUsers_UploadedById",
                table: "Files");

            migrationBuilder.DropIndex(
                name: "IX_Files_UploadedById",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "UploadedById",
                table: "Files");
        }
    }
}
