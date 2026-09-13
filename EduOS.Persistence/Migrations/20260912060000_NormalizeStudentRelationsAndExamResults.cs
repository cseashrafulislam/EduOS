using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912060000_NormalizeStudentRelationsAndExamResults")]
public partial class NormalizeStudentRelationsAndExamResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeStudentKey(migrationBuilder, "Enrollments");
        NormalizeStudentKey(migrationBuilder, "StudentAttendances");
        NormalizeStudentKey(migrationBuilder, "MarkEntries");
        NormalizeStudentKey(migrationBuilder, "ExamResults");

        migrationBuilder.Sql("IF COL_LENGTH('dbo.StudentAttendances','MarkedBy') IS NOT NULL ALTER TABLE [dbo].[StudentAttendances] ALTER COLUMN [MarkedBy] BIGINT NOT NULL;");
        migrationBuilder.Sql("IF COL_LENGTH('dbo.MarkEntries','EnteredBy') IS NOT NULL ALTER TABLE [dbo].[MarkEntries] ALTER COLUMN [EnteredBy] BIGINT NOT NULL;");

        migrationBuilder.AddColumn<int>(name: "AcademicYearId", table: "ExamResults", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "ClassId", table: "ExamResults", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "SectionId", table: "ExamResults", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "GroupId", table: "ExamResults", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "TotalFullMark", table: "ExamResults", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>(name: "Percentage", table: "ExamResults", type: "decimal(7,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<bool>(name: "IsPublished", table: "ExamResults", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>(name: "PublishedAtUtc", table: "ExamResults", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(name: "PublishedByUserId", table: "ExamResults", type: "bigint", nullable: true);

        migrationBuilder.Sql(@"UPDATE r SET AcademicYearId=s.AcademicYearId, ClassId=s.ClassId, SectionId=s.SectionId, GroupId=s.GroupId, TotalFullMark=CASE WHEN r.TotalMark > 0 THEN r.TotalMark ELSE 0 END, Percentage=CASE WHEN r.TotalMark > 0 THEN 100 ELSE 0 END FROM ExamResults r INNER JOIN Students s ON s.Id=r.StudentId WHERE r.AcademicYearId=0;");
        migrationBuilder.Sql(@";WITH d AS (SELECT Id, ROW_NUMBER() OVER(PARTITION BY TenantId,ExamId,StudentId,SubjectId ORDER BY Id DESC) rn FROM MarkEntries WHERE IsDeleted=0) DELETE FROM MarkEntries WHERE Id IN (SELECT Id FROM d WHERE rn>1);");
        migrationBuilder.Sql(@";WITH d AS (SELECT Id, ROW_NUMBER() OVER(PARTITION BY TenantId,ExamId,StudentId ORDER BY Id DESC) rn FROM ExamResults WHERE IsDeleted=0) DELETE FROM ExamResults WHERE Id IN (SELECT Id FROM d WHERE rn>1);");
        migrationBuilder.CreateIndex(name: "UX_MarkEntries_Tenant_Exam_Student_Subject", table: "MarkEntries", columns: new[] { "TenantId", "ExamId", "StudentId", "SubjectId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_ExamResults_Tenant_Exam_Student", table: "ExamResults", columns: new[] { "TenantId", "ExamId", "StudentId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_ExamResults_Tenant_Exam_Class_Section_Published", table: "ExamResults", columns: new[] { "TenantId", "ExamId", "ClassId", "SectionId", "IsPublished" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_MarkEntries_Tenant_Exam_Student_Subject", table: "MarkEntries");
        migrationBuilder.DropIndex(name: "UX_ExamResults_Tenant_Exam_Student", table: "ExamResults");
        migrationBuilder.DropIndex(name: "IX_ExamResults_Tenant_Exam_Class_Section_Published", table: "ExamResults");
        migrationBuilder.DropColumn(name: "AcademicYearId", table: "ExamResults");
        migrationBuilder.DropColumn(name: "ClassId", table: "ExamResults");
        migrationBuilder.DropColumn(name: "SectionId", table: "ExamResults");
        migrationBuilder.DropColumn(name: "GroupId", table: "ExamResults");
        migrationBuilder.DropColumn(name: "TotalFullMark", table: "ExamResults");
        migrationBuilder.DropColumn(name: "Percentage", table: "ExamResults");
        migrationBuilder.DropColumn(name: "IsPublished", table: "ExamResults");
        migrationBuilder.DropColumn(name: "PublishedAtUtc", table: "ExamResults");
        migrationBuilder.DropColumn(name: "PublishedByUserId", table: "ExamResults");
    }

    private static void NormalizeStudentKey(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.Sql($@"
DECLARE @sql nvarchar(max)=N'';
SELECT @sql += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + fk.name + N'];'
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id
JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
WHERE fkc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'StudentId',N'StudentId1');
IF LEN(@sql)>0 EXEC sp_executesql @sql;
IF COL_LENGTH('dbo.{table}','StudentId1') IS NOT NULL BEGIN
    IF COL_LENGTH('dbo.{table}','StudentId') IS NOT NULL EXEC(N'UPDATE [dbo].[{table}] SET [StudentId]=CONVERT(int,[StudentId1]) WHERE [StudentId1] IS NOT NULL AND ([StudentId] IS NULL OR [StudentId]=0)');
    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'StudentId1' AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;
    ALTER TABLE [dbo].[{table}] DROP COLUMN [StudentId1];
END;
IF COL_LENGTH('dbo.{table}','StudentId') IS NOT NULL BEGIN
    DECLARE @ix2 nvarchar(max)=N'';
    SELECT @ix2 += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'StudentId' AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix2)>0 EXEC sp_executesql @ix2;
    ALTER TABLE [dbo].[{table}] ALTER COLUMN [StudentId] BIGINT NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'IX_{table}_StudentId') CREATE INDEX [IX_{table}_StudentId] ON [dbo].[{table}]([StudentId]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'FK_{table}_Students_StudentId') ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [FK_{table}_Students_StudentId] FOREIGN KEY([StudentId]) REFERENCES [dbo].[Students]([Id]);
END;");
    }
}
