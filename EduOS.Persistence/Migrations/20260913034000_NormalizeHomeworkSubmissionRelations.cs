using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913034000_NormalizeHomeworkSubmissionRelations")]
public partial class NormalizeHomeworkSubmissionRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[HomeworkSubmissions]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[Homeworks]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Students]', N'U') IS NULL
        THROW 51000, 'Cannot normalize HomeworkSubmissions because one or more referenced tables do not exist.', 1;

    DECLARE @submissionFk nvarchar(max)=N'';
    SELECT @submissionFk += N'ALTER TABLE [dbo].[HomeworkSubmissions] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[HomeworkSubmissions]') AND c.name IN (N'HomeworkId',N'HomeworkId1',N'StudentId',N'StudentId1');
    IF LEN(@submissionFk)>0 EXEC sp_executesql @submissionFk;

    DECLARE @submissionIx nvarchar(max)=N'';
    SELECT @submissionIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[HomeworkSubmissions];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[HomeworkSubmissions]')
      AND c.name IN (N'HomeworkId',N'HomeworkId1',N'StudentId',N'StudentId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@submissionIx)>0 EXEC sp_executesql @submissionIx;

    IF COL_LENGTH('dbo.HomeworkSubmissions','HomeworkId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] WHERE [HomeworkId1] IS NOT NULL AND [HomeworkId] IS NOT NULL AND CONVERT(bigint,[HomeworkId])<>CONVERT(bigint,[HomeworkId1]))
            THROW 51000, 'Cannot normalize HomeworkSubmissions.HomeworkId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[HomeworkSubmissions] ADD [HomeworkIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[HomeworkSubmissions] SET [HomeworkIdNormalized]=COALESCE(CONVERT(bigint,[HomeworkId1]),CONVERT(bigint,[HomeworkId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] WHERE [HomeworkIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize HomeworkSubmissions.HomeworkId because required references are null.', 1;
        ALTER TABLE [dbo].[HomeworkSubmissions] DROP COLUMN [HomeworkId];
        ALTER TABLE [dbo].[HomeworkSubmissions] DROP COLUMN [HomeworkId1];
        EXEC sp_rename N'dbo.HomeworkSubmissions.HomeworkIdNormalized', N'HomeworkId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[HomeworkSubmissions] ALTER COLUMN [HomeworkId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.HomeworkSubmissions','StudentId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] WHERE [StudentId1] IS NOT NULL AND [StudentId] IS NOT NULL AND CONVERT(bigint,[StudentId])<>CONVERT(bigint,[StudentId1]))
            THROW 51000, 'Cannot normalize HomeworkSubmissions.StudentId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[HomeworkSubmissions] ADD [StudentIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[HomeworkSubmissions] SET [StudentIdNormalized]=COALESCE(CONVERT(bigint,[StudentId1]),CONVERT(bigint,[StudentId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] WHERE [StudentIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize HomeworkSubmissions.StudentId because required references are null.', 1;
        ALTER TABLE [dbo].[HomeworkSubmissions] DROP COLUMN [StudentId];
        ALTER TABLE [dbo].[HomeworkSubmissions] DROP COLUMN [StudentId1];
        EXEC sp_rename N'dbo.HomeworkSubmissions.StudentIdNormalized', N'StudentId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[HomeworkSubmissions] ALTER COLUMN [StudentId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Homeworks] p WHERE p.[Id]=r.[HomeworkId]))
        THROW 51000, 'Cannot normalize HomeworkSubmissions.HomeworkId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] r JOIN [dbo].[Homeworks] p ON p.[Id]=r.[HomeworkId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize HomeworkSubmissions.HomeworkId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Students] p WHERE p.[Id]=r.[StudentId]))
        THROW 51000, 'Cannot normalize HomeworkSubmissions.StudentId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[HomeworkSubmissions] r JOIN [dbo].[Students] p ON p.[Id]=r.[StudentId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize HomeworkSubmissions.StudentId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_HomeworkSubmissions_HomeworkId] ON [dbo].[HomeworkSubmissions]([HomeworkId]);
    CREATE INDEX [IX_HomeworkSubmissions_StudentId] ON [dbo].[HomeworkSubmissions]([StudentId]);
    ALTER TABLE [dbo].[HomeworkSubmissions] WITH CHECK ADD CONSTRAINT [FK_HomeworkSubmissions_Homeworks_HomeworkId] FOREIGN KEY([HomeworkId]) REFERENCES [dbo].[Homeworks]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[HomeworkSubmissions] WITH CHECK ADD CONSTRAINT [FK_HomeworkSubmissions_Students_StudentId] FOREIGN KEY([StudentId]) REFERENCES [dbo].[Students]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
