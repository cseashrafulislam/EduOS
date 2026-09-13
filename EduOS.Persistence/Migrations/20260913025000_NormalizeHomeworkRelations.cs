using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913025000_NormalizeHomeworkRelations")]
public partial class NormalizeHomeworkRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Homeworks]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[Classes]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Sections]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Subjects]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Employees]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Homeworks because one or more referenced tables do not exist.', 1;

    DECLARE @homeworkFk nvarchar(max)=N'';
    SELECT @homeworkFk += N'ALTER TABLE [dbo].[Homeworks] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Homeworks]') AND c.name IN (N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1',N'TeacherId',N'TeacherId1');
    IF LEN(@homeworkFk)>0 EXEC sp_executesql @homeworkFk;

    DECLARE @homeworkIx nvarchar(max)=N'';
    SELECT @homeworkIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[Homeworks];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Homeworks]')
      AND c.name IN (N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1',N'TeacherId',N'TeacherId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@homeworkIx)>0 EXEC sp_executesql @homeworkIx;

    IF COL_LENGTH('dbo.Homeworks','ClassId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [ClassId1] IS NOT NULL AND [ClassId] IS NOT NULL AND CONVERT(bigint,[ClassId])<>CONVERT(bigint,[ClassId1]))
            THROW 51000, 'Cannot normalize Homeworks.ClassId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Homeworks] ADD [ClassIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Homeworks] SET [ClassIdNormalized]=COALESCE(CONVERT(bigint,[ClassId1]),CONVERT(bigint,[ClassId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [ClassIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Homeworks.ClassId because required references are null.', 1;
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [ClassId];
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [ClassId1];
        EXEC sp_rename N'dbo.Homeworks.ClassIdNormalized', N'ClassId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Homeworks] ALTER COLUMN [ClassId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Homeworks','SectionId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [SectionId1] IS NOT NULL AND [SectionId] IS NOT NULL AND CONVERT(bigint,[SectionId])<>CONVERT(bigint,[SectionId1]))
            THROW 51000, 'Cannot normalize Homeworks.SectionId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Homeworks] ADD [SectionIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Homeworks] SET [SectionIdNormalized]=COALESCE(CONVERT(bigint,[SectionId1]),CONVERT(bigint,[SectionId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [SectionIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Homeworks.SectionId because required references are null.', 1;
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [SectionId];
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [SectionId1];
        EXEC sp_rename N'dbo.Homeworks.SectionIdNormalized', N'SectionId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Homeworks] ALTER COLUMN [SectionId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Homeworks','SubjectId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [SubjectId1] IS NOT NULL AND [SubjectId] IS NOT NULL AND CONVERT(bigint,[SubjectId])<>CONVERT(bigint,[SubjectId1]))
            THROW 51000, 'Cannot normalize Homeworks.SubjectId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Homeworks] ADD [SubjectIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Homeworks] SET [SubjectIdNormalized]=COALESCE(CONVERT(bigint,[SubjectId1]),CONVERT(bigint,[SubjectId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [SubjectIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Homeworks.SubjectId because required references are null.', 1;
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [SubjectId];
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [SubjectId1];
        EXEC sp_rename N'dbo.Homeworks.SubjectIdNormalized', N'SubjectId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Homeworks] ALTER COLUMN [SubjectId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Homeworks','TeacherId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [TeacherId1] IS NOT NULL AND [TeacherId] IS NOT NULL AND CONVERT(bigint,[TeacherId])<>CONVERT(bigint,[TeacherId1]))
            THROW 51000, 'Cannot normalize Homeworks.TeacherId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Homeworks] ADD [TeacherIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Homeworks] SET [TeacherIdNormalized]=COALESCE(CONVERT(bigint,[TeacherId1]),CONVERT(bigint,[TeacherId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] WHERE [TeacherIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Homeworks.TeacherId because required references are null.', 1;
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [TeacherId];
        ALTER TABLE [dbo].[Homeworks] DROP COLUMN [TeacherId1];
        EXEC sp_rename N'dbo.Homeworks.TeacherIdNormalized', N'TeacherId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Homeworks] ALTER COLUMN [TeacherId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Classes] p WHERE p.[Id]=r.[ClassId]))
        THROW 51000, 'Cannot normalize Homeworks.ClassId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r JOIN [dbo].[Classes] p ON p.[Id]=r.[ClassId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Homeworks.ClassId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Sections] p WHERE p.[Id]=r.[SectionId]))
        THROW 51000, 'Cannot normalize Homeworks.SectionId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r JOIN [dbo].[Sections] p ON p.[Id]=r.[SectionId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Homeworks.SectionId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Subjects] p WHERE p.[Id]=r.[SubjectId]))
        THROW 51000, 'Cannot normalize Homeworks.SubjectId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r JOIN [dbo].[Subjects] p ON p.[Id]=r.[SubjectId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Homeworks.SubjectId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Employees] p WHERE p.[Id]=r.[TeacherId]))
        THROW 51000, 'Cannot normalize Homeworks.TeacherId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Homeworks] r JOIN [dbo].[Employees] p ON p.[Id]=r.[TeacherId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Homeworks.TeacherId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_Homeworks_ClassId] ON [dbo].[Homeworks]([ClassId]);
    CREATE INDEX [IX_Homeworks_SectionId] ON [dbo].[Homeworks]([SectionId]);
    CREATE INDEX [IX_Homeworks_SubjectId] ON [dbo].[Homeworks]([SubjectId]);
    CREATE INDEX [IX_Homeworks_TeacherId] ON [dbo].[Homeworks]([TeacherId]);

    ALTER TABLE [dbo].[Homeworks] WITH CHECK ADD CONSTRAINT [FK_Homeworks_Classes_ClassId] FOREIGN KEY([ClassId]) REFERENCES [dbo].[Classes]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Homeworks] WITH CHECK ADD CONSTRAINT [FK_Homeworks_Sections_SectionId] FOREIGN KEY([SectionId]) REFERENCES [dbo].[Sections]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Homeworks] WITH CHECK ADD CONSTRAINT [FK_Homeworks_Subjects_SubjectId] FOREIGN KEY([SubjectId]) REFERENCES [dbo].[Subjects]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Homeworks] WITH CHECK ADD CONSTRAINT [FK_Homeworks_Employees_TeacherId] FOREIGN KEY([TeacherId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
