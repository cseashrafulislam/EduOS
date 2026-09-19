using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913024000_NormalizeLiveClassRelations")]
public partial class NormalizeLiveClassRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[LiveClasses]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[Classes]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Sections]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Subjects]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Employees]', N'U') IS NULL
        THROW 51000, 'Cannot normalize LiveClasses because one or more referenced tables do not exist.', 1;

    DECLARE @liveFk nvarchar(max)=N'';
    SELECT @liveFk += N'ALTER TABLE [dbo].[LiveClasses] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[LiveClasses]') AND c.name IN (N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1',N'TeacherId',N'TeacherId1');
    IF LEN(@liveFk)>0 EXEC sp_executesql @liveFk;

    DECLARE @liveIx nvarchar(max)=N'';
    SELECT @liveIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[LiveClasses];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[LiveClasses]')
      AND c.name IN (N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1',N'TeacherId',N'TeacherId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@liveIx)>0 EXEC sp_executesql @liveIx;

    IF COL_LENGTH('dbo.LiveClasses','ClassId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [ClassId1] IS NOT NULL AND [ClassId] IS NOT NULL AND CONVERT(bigint,[ClassId])<>CONVERT(bigint,[ClassId1]))
            THROW 51000, 'Cannot normalize LiveClasses.ClassId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[LiveClasses] ADD [ClassIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[LiveClasses] SET [ClassIdNormalized]=COALESCE(CONVERT(bigint,[ClassId1]),CONVERT(bigint,[ClassId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [ClassIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize LiveClasses.ClassId because required references are null.', 1;
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [ClassId];
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [ClassId1];
        EXEC sp_rename N'dbo.LiveClasses.ClassIdNormalized', N'ClassId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[LiveClasses] ALTER COLUMN [ClassId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.LiveClasses','SectionId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [SectionId1] IS NOT NULL AND [SectionId] IS NOT NULL AND CONVERT(bigint,[SectionId])<>CONVERT(bigint,[SectionId1]))
            THROW 51000, 'Cannot normalize LiveClasses.SectionId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[LiveClasses] ADD [SectionIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[LiveClasses] SET [SectionIdNormalized]=COALESCE(CONVERT(bigint,[SectionId1]),CONVERT(bigint,[SectionId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [SectionIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize LiveClasses.SectionId because required references are null.', 1;
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [SectionId];
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [SectionId1];
        EXEC sp_rename N'dbo.LiveClasses.SectionIdNormalized', N'SectionId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[LiveClasses] ALTER COLUMN [SectionId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.LiveClasses','SubjectId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [SubjectId1] IS NOT NULL AND [SubjectId] IS NOT NULL AND CONVERT(bigint,[SubjectId])<>CONVERT(bigint,[SubjectId1]))
            THROW 51000, 'Cannot normalize LiveClasses.SubjectId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[LiveClasses] ADD [SubjectIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[LiveClasses] SET [SubjectIdNormalized]=COALESCE(CONVERT(bigint,[SubjectId1]),CONVERT(bigint,[SubjectId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [SubjectIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize LiveClasses.SubjectId because required references are null.', 1;
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [SubjectId];
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [SubjectId1];
        EXEC sp_rename N'dbo.LiveClasses.SubjectIdNormalized', N'SubjectId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[LiveClasses] ALTER COLUMN [SubjectId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.LiveClasses','TeacherId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [TeacherId1] IS NOT NULL AND [TeacherId] IS NOT NULL AND CONVERT(bigint,[TeacherId])<>CONVERT(bigint,[TeacherId1]))
            THROW 51000, 'Cannot normalize LiveClasses.TeacherId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[LiveClasses] ADD [TeacherIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[LiveClasses] SET [TeacherIdNormalized]=COALESCE(CONVERT(bigint,[TeacherId1]),CONVERT(bigint,[TeacherId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] WHERE [TeacherIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize LiveClasses.TeacherId because required references are null.', 1;
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [TeacherId];
        ALTER TABLE [dbo].[LiveClasses] DROP COLUMN [TeacherId1];
        EXEC sp_rename N'dbo.LiveClasses.TeacherIdNormalized', N'TeacherId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[LiveClasses] ALTER COLUMN [TeacherId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Classes] p WHERE p.[Id]=r.[ClassId]))
        THROW 51000, 'Cannot normalize LiveClasses.ClassId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r JOIN [dbo].[Classes] p ON p.[Id]=r.[ClassId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize LiveClasses.ClassId because cross-tenant references exist.', 1;

    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Sections] p WHERE p.[Id]=r.[SectionId]))
        THROW 51000, 'Cannot normalize LiveClasses.SectionId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r JOIN [dbo].[Sections] p ON p.[Id]=r.[SectionId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize LiveClasses.SectionId because cross-tenant references exist.', 1;

    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Subjects] p WHERE p.[Id]=r.[SubjectId]))
        THROW 51000, 'Cannot normalize LiveClasses.SubjectId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r JOIN [dbo].[Subjects] p ON p.[Id]=r.[SubjectId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize LiveClasses.SubjectId because cross-tenant references exist.', 1;

    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Employees] p WHERE p.[Id]=r.[TeacherId]))
        THROW 51000, 'Cannot normalize LiveClasses.TeacherId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[LiveClasses] r JOIN [dbo].[Employees] p ON p.[Id]=r.[TeacherId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize LiveClasses.TeacherId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_LiveClasses_ClassId] ON [dbo].[LiveClasses]([ClassId]);
    CREATE INDEX [IX_LiveClasses_SectionId] ON [dbo].[LiveClasses]([SectionId]);
    CREATE INDEX [IX_LiveClasses_SubjectId] ON [dbo].[LiveClasses]([SubjectId]);
    CREATE INDEX [IX_LiveClasses_TeacherId] ON [dbo].[LiveClasses]([TeacherId]);

    ALTER TABLE [dbo].[LiveClasses] WITH CHECK ADD CONSTRAINT [FK_LiveClasses_Classes_ClassId] FOREIGN KEY([ClassId]) REFERENCES [dbo].[Classes]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[LiveClasses] WITH CHECK ADD CONSTRAINT [FK_LiveClasses_Sections_SectionId] FOREIGN KEY([SectionId]) REFERENCES [dbo].[Sections]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[LiveClasses] WITH CHECK ADD CONSTRAINT [FK_LiveClasses_Subjects_SubjectId] FOREIGN KEY([SubjectId]) REFERENCES [dbo].[Subjects]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[LiveClasses] WITH CHECK ADD CONSTRAINT [FK_LiveClasses_Employees_TeacherId] FOREIGN KEY([TeacherId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
