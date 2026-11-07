using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GZCTF.Migrations
{
    /// <inheritdoc />
    public partial class WriteupByWeek : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChallengeWriteups_ParticipationId_ChallengeId",
                table: "ChallengeWriteups");

            migrationBuilder.AlterColumn<int>(
                name: "ChallengeId",
                table: "ChallengeWriteups",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Week",
                table: "ChallengeWriteups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 历史按题记录：按题目所属周次回填，便于页面显示「第几周」
            migrationBuilder.Sql("""
                UPDATE "ChallengeWriteups" w
                SET "Week" = c."Week"
                FROM "GameChallenges" c
                WHERE c."Id" = w."ChallengeId" AND w."Week" = 0;
            """);

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeWriteups_ParticipationId_Week",
                table: "ChallengeWriteups",
                columns: new[] { "ParticipationId", "Week" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChallengeWriteups_ParticipationId_Week",
                table: "ChallengeWriteups");

            migrationBuilder.DropColumn(
                name: "Week",
                table: "ChallengeWriteups");

            migrationBuilder.AlterColumn<int>(
                name: "ChallengeId",
                table: "ChallengeWriteups",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeWriteups_ParticipationId_ChallengeId",
                table: "ChallengeWriteups",
                columns: new[] { "ParticipationId", "ChallengeId" },
                unique: true);
        }
    }
}
