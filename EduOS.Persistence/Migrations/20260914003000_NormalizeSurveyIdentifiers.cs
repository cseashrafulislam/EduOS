using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    public partial class NormalizeSurveyIdentifiers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM [SurveyQuestions]
    WHERE [SurveyId1] IS NOT NULL AND [SurveyId] > 0 AND CONVERT(bigint, [SurveyId]) <> [SurveyId1])
    THROW 51000, 'SurveyQuestions contains conflicting SurveyId and SurveyId1 values.', 1;

IF EXISTS (
    SELECT 1 FROM [SurveyResponses]
    WHERE [SurveyId1] IS NOT NULL AND [SurveyId] > 0 AND CONVERT(bigint, [SurveyId]) <> [SurveyId1])
    THROW 51000, 'SurveyResponses contains conflicting SurveyId and SurveyId1 values.', 1;

IF EXISTS (
    SELECT 1 FROM [SurveyResponses]
    WHERE [QuestionId1] IS NOT NULL AND [QuestionId] > 0 AND CONVERT(bigint, [QuestionId]) <> [QuestionId1])
    THROW 51000, 'SurveyResponses contains conflicting QuestionId and QuestionId1 values.', 1;

IF EXISTS (
    SELECT 1 FROM [SurveyQuestions] q
    WHERE COALESCE(NULLIF(CONVERT(bigint, q.[SurveyId]), 0), q.[SurveyId1]) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM [Surveys] s
           WHERE s.[Id] = COALESCE(NULLIF(CONVERT(bigint, q.[SurveyId]), 0), q.[SurveyId1])))
    THROW 51000, 'SurveyQuestions contains a missing or orphan survey reference.', 1;

IF EXISTS (
    SELECT 1 FROM [SurveyResponses] r
    WHERE COALESCE(NULLIF(CONVERT(bigint, r.[SurveyId]), 0), r.[SurveyId1]) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM [Surveys] s
           WHERE s.[Id] = COALESCE(NULLIF(CONVERT(bigint, r.[SurveyId]), 0), r.[SurveyId1])))
    THROW 51000, 'SurveyResponses contains a missing or orphan survey reference.', 1;

IF EXISTS (
    SELECT 1 FROM [SurveyResponses] r
    WHERE COALESCE(NULLIF(CONVERT(bigint, r.[QuestionId]), 0), r.[QuestionId1]) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM [SurveyQuestions] q
           WHERE q.[Id] = COALESCE(NULLIF(CONVERT(bigint, r.[QuestionId]), 0), r.[QuestionId1])))
    THROW 51000, 'SurveyResponses contains a missing or orphan question reference.', 1;

IF OBJECT_ID(N'[FK_SurveyQuestions_Surveys_SurveyId1]', 'F') IS NOT NULL
    ALTER TABLE [SurveyQuestions] DROP CONSTRAINT [FK_SurveyQuestions_Surveys_SurveyId1];
IF OBJECT_ID(N'[FK_SurveyResponses_Surveys_SurveyId1]', 'F') IS NOT NULL
    ALTER TABLE [SurveyResponses] DROP CONSTRAINT [FK_SurveyResponses_Surveys_SurveyId1];
IF OBJECT_ID(N'[FK_SurveyResponses_SurveyQuestions_QuestionId1]', 'F') IS NOT NULL
    ALTER TABLE [SurveyResponses] DROP CONSTRAINT [FK_SurveyResponses_SurveyQuestions_QuestionId1];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SurveyQuestions_SurveyId1' AND object_id = OBJECT_ID(N'[SurveyQuestions]'))
    DROP INDEX [IX_SurveyQuestions_SurveyId1] ON [SurveyQuestions];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SurveyResponses_SurveyId1' AND object_id = OBJECT_ID(N'[SurveyResponses]'))
    DROP INDEX [IX_SurveyResponses_SurveyId1] ON [SurveyResponses];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SurveyResponses_QuestionId1' AND object_id = OBJECT_ID(N'[SurveyResponses]'))
    DROP INDEX [IX_SurveyResponses_QuestionId1] ON [SurveyResponses];
");

            migrationBuilder.AlterColumn<long>(
                name: "SurveyId",
                table: "SurveyQuestions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "SurveyId",
                table: "SurveyResponses",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "QuestionId",
                table: "SurveyResponses",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "RespondentId",
                table: "SurveyResponses",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.Sql(@"
UPDATE [SurveyQuestions]
SET [SurveyId] = [SurveyId1]
WHERE [SurveyId] <= 0 AND [SurveyId1] IS NOT NULL;

UPDATE [SurveyResponses]
SET [SurveyId] = [SurveyId1]
WHERE [SurveyId] <= 0 AND [SurveyId1] IS NOT NULL;

UPDATE [SurveyResponses]
SET [QuestionId] = [QuestionId1]
WHERE [QuestionId] <= 0 AND [QuestionId1] IS NOT NULL;
");

            migrationBuilder.DropColumn(name: "SurveyId1", table: "SurveyQuestions");
            migrationBuilder.DropColumn(name: "SurveyId1", table: "SurveyResponses");
            migrationBuilder.DropColumn(name: "QuestionId1", table: "SurveyResponses");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyQuestions_SurveyId",
                table: "SurveyQuestions",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_SurveyId",
                table: "SurveyResponses",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_QuestionId",
                table: "SurveyResponses",
                column: "QuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyQuestions_Surveys_SurveyId",
                table: "SurveyQuestions",
                column: "SurveyId",
                principalTable: "Surveys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_Surveys_SurveyId",
                table: "SurveyResponses",
                column: "SurveyId",
                principalTable: "Surveys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SurveyResponses_SurveyQuestions_QuestionId",
                table: "SurveyResponses",
                column: "QuestionId",
                principalTable: "SurveyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Survey identifiers cannot be safely narrowed from bigint to int after normalization.");
        }
    }
}
