using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913073000_NormalizeOnlineExamRelations")]
public partial class NormalizeOnlineExamRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeTenantRelation(migrationBuilder, "Questions", "SubjectId", "Subjects");
        NormalizeTenantRelation(migrationBuilder, "OnlineExams", "ClassId", "Classes");
        NormalizeTenantRelation(migrationBuilder, "OnlineExams", "SubjectId", "Subjects");

        NormalizeRelation(migrationBuilder, "OnlineExamQuestions", "OnlineExamId", "OnlineExams");
        NormalizeRelation(migrationBuilder, "OnlineExamQuestions", "QuestionId", "Questions");
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[OnlineExamQuestions]', N'U') IS NOT NULL
AND EXISTS(
    SELECT 1 FROM [dbo].[OnlineExamQuestions] x
    JOIN [dbo].[OnlineExams] e ON e.[Id]=x.[OnlineExamId]
    JOIN [dbo].[Questions] q ON q.[Id]=x.[QuestionId]
    WHERE e.[TenantId]<>q.[TenantId])
    THROW 51000, 'Cannot normalize OnlineExamQuestions because cross-tenant exam/question references exist.', 1;");

        NormalizeRelation(migrationBuilder, "OnlineExamAttempts", "OnlineExamId", "OnlineExams");
        NormalizeRelation(migrationBuilder, "OnlineExamAttempts", "StudentId", "Students");
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[OnlineExamAttempts]', N'U') IS NOT NULL
AND EXISTS(
    SELECT 1 FROM [dbo].[OnlineExamAttempts] x
    JOIN [dbo].[OnlineExams] e ON e.[Id]=x.[OnlineExamId]
    JOIN [dbo].[Students] s ON s.[Id]=x.[StudentId]
    WHERE e.[TenantId]<>s.[TenantId])
    THROW 51000, 'Cannot normalize OnlineExamAttempts because cross-tenant exam/student references exist.', 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeTenantRelation(MigrationBuilder migrationBuilder, string table, string column, string parentTable)
    {
        NormalizeRelation(migrationBuilder, table, column, parentTable);
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
AND EXISTS(SELECT 1 FROM [dbo].[{table}] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[TenantId]<>p.[TenantId])
    THROW 51000, 'Cannot normalize {table}.{column} because cross-tenant references exist.', 1;");
    }

    private static void NormalizeRelation(MigrationBuilder migrationBuilder, string table, string column, string parentTable)
    {
        var shadow = column + "1";
        var normalized = column + "Normalized";
        var indexName = $"IX_{table}_{column}";
        var fkName = $"FK_{table}_{parentTable}_{column}";

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize {table}.{column} because {parentTable} does not exist.', 1;
    IF COL_LENGTH('dbo.{table}','{column}') IS NULL
        THROW 51000, 'Cannot normalize {table}.{column} because the canonical column does not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.{table}','{shadow}') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[{table}] WHERE [{shadow}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadow}]))
            THROW 51000, 'Cannot normalize {table}.{column} because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.{table}','{normalized}') IS NOT NULL
            ALTER TABLE [dbo].[{table}] DROP COLUMN [{normalized}];
        ALTER TABLE [dbo].[{table}] ADD [{normalized}] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[{table}] SET [{normalized}]=COALESCE(CONVERT(bigint,[{shadow}]),NULLIF(CONVERT(bigint,[{column}]),0));');
        IF EXISTS(SELECT 1 FROM [dbo].[{table}] WHERE [{normalized}] IS NULL)
            THROW 51000, 'Cannot normalize {table}.{column} because required references are null.', 1;
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{shadow}];
        EXEC sp_rename N'dbo.{table}.{normalized}', N'{column}', 'COLUMN';
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NOT NULL;
    END
    ELSE
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[{table}] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize {table}.{column} because orphaned references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[{table}]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
