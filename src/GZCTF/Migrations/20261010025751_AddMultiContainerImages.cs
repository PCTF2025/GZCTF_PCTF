using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiContainerImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 默认值为空 JSON 数组，避免历史行反序列化失败
            migrationBuilder.AddColumn<string>(
                name: "ExtraContainerImages",
                table: "GameChallenges",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Containers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "Containers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupOrder",
                table: "Containers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraContainerImages",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "GroupName",
                table: "Containers");

            migrationBuilder.DropColumn(
                name: "GroupOrder",
                table: "Containers");
        }
    }
}
