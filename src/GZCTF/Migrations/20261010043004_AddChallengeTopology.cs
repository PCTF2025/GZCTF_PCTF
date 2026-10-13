using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class AddChallengeTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TopologyEdges",
                table: "GameChallenges",
                type: "text",
                nullable: false,
                // 空串无法反序列化为 JSON 列表，历史行必须落 "[]"
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "TopologyNodes",
                table: "GameChallenges",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TopologyEdges",
                table: "GameChallenges");

            migrationBuilder.DropColumn(
                name: "TopologyNodes",
                table: "GameChallenges");
        }
    }
}
