using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913064000_NormalizeExamFamilyRelations")]
public partial class NormalizeExamFamilyRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        DropKnownCompositeIndexes(migrationBuilder);

        NormalizeRequiredRelation(migrationBuilder, "Exams", "AcademicYearId", "AcademicYears");

        NormalizeRequiredRelation(migrationBuilder, "ExamSchedules", "ExamId", "Exams");
        NormalizeRequiredRelation(migrationBuilder, "ExamSchedules", "ClassId", "Classes");
        NormalizeRequiredRelation(migrationBuilder, "ExamSchedules", "SubjectId", "Subjects");

        NormalizeRequiredRelation(migrationBuilder, "ExamResults", "ExamId", "Exams");
        NormalizeRequiredRelation(migrationBuilder, "ExamResults", "StudentId", "Students");
        WidenSnapshotColumn(migrationBuilder, "ExamResults", "AcademicYearId", false);
        WidenSnapshotColumn(migrationBuilder, "ExamResults", "ClassId", false);
        WidenSnapshotColumn(migrationBuilder, "ExamResults", "SectionId", false);
        WidenSnapshotColumn(migrationBuilder, "ExamResults", "GroupId", true);

        NormalizeRequiredRelation(migrationBuilder, "MarkEntries", "ExamId", "Exams");
        NormalizeRequiredRelation(migrationBuilder, "MarkEntries", "StudentId", "Students");
        NormalizeRequiredRelation(migrationBuilder, "MarkEntries", "SubjectId", "Subjects");
        WidenSnapshotColumn(migrationBuilder, "MarkEntries", "EnteredBy", false);

        RecreateKnownCompositeIndexes(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void DropKnownCompositeIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[MarkEntries]', N'U') IS NOT NULL AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[MarkEntries]') AND name=N'UX_MarkEntries_Tenant_Exam_Student_Subject')
    DROP INDEX [UX_MarkEntries_Tenant_Exam_Student_Subject] ON [dbo].[MarkEntries];
IF OBJECT_ID(N'[dbo].[ExamResults]', N'U') IS NOT NULL AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[ExamResults]') AND name=N'UX_ExamResults_Tenant_Exam_Student')
    DROP INDEX [UX_ExamResults_Tenant_Exam_Student] ON [dbo].[ExamResults];
IF OBJECT_ID(N'[dbo].[ExamResults]', N'U') IS NOT NULL AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[ExamResults]') AND name=N'IX_ExamResults_Tenant_Exam_Class_Section_Published')
    DROP INDEX [IX_ExamResults_Tenant_Exam_Class_Section_Published] ON [dbo].[ExamResults];");
    }

    private static void RecreateKnownCompositeIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[MarkEntries]', N'U') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[MarkEntries]') AND name=N'UX_MarkEntries_Tenant_Exam_Student_Subject')
    CREATE UNIQUE INDEX [UX_MarkEntries_Tenant_Exam_Student_Subject] ON [dbo].[MarkEntries]([TenantId],[ExamId],[StudentId],[SubjectId]) WHERE [IsDeleted]=0;
IF OBJECT_ID(N'[dbo].[ExamResults]', N'U') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[ExamResults]') AND name=N'UX_ExamResults_Tenant_Exam_Student')
    CREATE UNIQUE INDEX [UX_ExamResults_Tenant_Exam_Student] ON [dbo].[ExamResults]([TenantId],[ExamId],[StudentId]) WHERE [IsDeleted]=0;
IF OBJECT_ID(N'[dbo].[ExamResults]', N'U') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[ExamResults]') AND name=N'IX_ExamResults_Tenant_Exam_Class_Section_Published')
    CREATE INDEX [IX_ExamResults_Tenant_Exam_Class_Section_Published] ON [dbo].[ExamResults]([TenantId],[ExamId],[ClassId],[SectionId],[IsPublished]);");
    }

    private static void WidenSnapshotColumn(MigrationBuilder migrationBuilder, string table, string column, bool nullable)
    {
        var nullability = nullable ? "NULL" : "NOT NULL";
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL AND COL_LENGTH('dbo.{table}','{column}') IS NOT NULL
    ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT {nullability};");
    }

    private static void NormalizeRequiredRelation(MigrationBuilder migrationBuilder, string table, string column, string parentTable)
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

    DECLARE @fk_{table}_{column} nvarchar(max)=N'';
    SELECT @fk_{table}_{column} += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk_{table}_{column})>0 EXEC sp_executesql @fk_{table}_{column};

    DECLARE @ix_{table}_{column} nvarchar(max)=N'';
    SELECT @ix_{table}_{column} += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix_{table}_{column})>0 EXEC sp_executesql @ix_{table}_{column};

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
    END
    ELSE
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[{table}] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize {table}.{column} because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[{table}] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize {table}.{column} because cross-tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[{table}]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
