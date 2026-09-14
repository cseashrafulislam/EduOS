using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913034500_NormalizeCourseRelations")]
public partial class NormalizeCourseRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Courses]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[AcademicYears]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Classes]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Sections]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Subjects]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Courses because one or more referenced tables do not exist.', 1;

    DECLARE @courseFk nvarchar(max)=N'';
    SELECT @courseFk += N'ALTER TABLE [dbo].[Courses] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Courses]') AND c.name IN (N'AcademicYearId',N'AcademicYearId1',N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1');
    IF LEN(@courseFk)>0 EXEC sp_executesql @courseFk;

    DECLARE @courseIx nvarchar(max)=N'';
    SELECT @courseIx += N'DROP INDEX ['+i.name+N'] ON [dbo].[Courses];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Courses]')
      AND c.name IN (N'AcademicYearId',N'AcademicYearId1',N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'SubjectId',N'SubjectId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@courseIx)>0 EXEC sp_executesql @courseIx;

    IF COL_LENGTH('dbo.Courses','AcademicYearId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [AcademicYearId1] IS NOT NULL AND [AcademicYearId] IS NOT NULL AND CONVERT(bigint,[AcademicYearId])<>CONVERT(bigint,[AcademicYearId1]))
            THROW 51000, 'Cannot normalize Courses.AcademicYearId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Courses] ADD [AcademicYearIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Courses] SET [AcademicYearIdNormalized]=COALESCE(CONVERT(bigint,[AcademicYearId1]),CONVERT(bigint,[AcademicYearId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [AcademicYearIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Courses.AcademicYearId because required references are null.', 1;
        ALTER TABLE [dbo].[Courses] DROP COLUMN [AcademicYearId];
        ALTER TABLE [dbo].[Courses] DROP COLUMN [AcademicYearId1];
        EXEC sp_rename N'dbo.Courses.AcademicYearIdNormalized', N'AcademicYearId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Courses] ALTER COLUMN [AcademicYearId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Courses','ClassId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [ClassId1] IS NOT NULL AND [ClassId] IS NOT NULL AND CONVERT(bigint,[ClassId])<>CONVERT(bigint,[ClassId1]))
            THROW 51000, 'Cannot normalize Courses.ClassId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Courses] ADD [ClassIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Courses] SET [ClassIdNormalized]=COALESCE(CONVERT(bigint,[ClassId1]),CONVERT(bigint,[ClassId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [ClassIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Courses.ClassId because required references are null.', 1;
        ALTER TABLE [dbo].[Courses] DROP COLUMN [ClassId];
        ALTER TABLE [dbo].[Courses] DROP COLUMN [ClassId1];
        EXEC sp_rename N'dbo.Courses.ClassIdNormalized', N'ClassId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Courses] ALTER COLUMN [ClassId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Courses','SectionId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [SectionId1] IS NOT NULL AND [SectionId] IS NOT NULL AND CONVERT(bigint,[SectionId])<>CONVERT(bigint,[SectionId1]))
            THROW 51000, 'Cannot normalize Courses.SectionId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Courses] ADD [SectionIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Courses] SET [SectionIdNormalized]=COALESCE(CONVERT(bigint,[SectionId1]),CONVERT(bigint,[SectionId]));');
        ALTER TABLE [dbo].[Courses] DROP COLUMN [SectionId];
        ALTER TABLE [dbo].[Courses] DROP COLUMN [SectionId1];
        EXEC sp_rename N'dbo.Courses.SectionIdNormalized', N'SectionId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Courses] ALTER COLUMN [SectionId] BIGINT NULL;

    IF COL_LENGTH('dbo.Courses','SubjectId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [SubjectId1] IS NOT NULL AND [SubjectId] IS NOT NULL AND CONVERT(bigint,[SubjectId])<>CONVERT(bigint,[SubjectId1]))
            THROW 51000, 'Cannot normalize Courses.SubjectId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Courses] ADD [SubjectIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Courses] SET [SubjectIdNormalized]=COALESCE(CONVERT(bigint,[SubjectId1]),CONVERT(bigint,[SubjectId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Courses] WHERE [SubjectIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize Courses.SubjectId because required references are null.', 1;
        ALTER TABLE [dbo].[Courses] DROP COLUMN [SubjectId];
        ALTER TABLE [dbo].[Courses] DROP COLUMN [SubjectId1];
        EXEC sp_rename N'dbo.Courses.SubjectIdNormalized', N'SubjectId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Courses] ALTER COLUMN [SubjectId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[AcademicYears] p WHERE p.[Id]=r.[AcademicYearId]))
        THROW 51000, 'Cannot normalize Courses.AcademicYearId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r JOIN [dbo].[AcademicYears] p ON p.[Id]=r.[AcademicYearId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Courses.AcademicYearId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Classes] p WHERE p.[Id]=r.[ClassId]))
        THROW 51000, 'Cannot normalize Courses.ClassId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r JOIN [dbo].[Classes] p ON p.[Id]=r.[ClassId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Courses.ClassId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r WHERE r.[SectionId] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[Sections] p WHERE p.[Id]=r.[SectionId]))
        THROW 51000, 'Cannot normalize Courses.SectionId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r JOIN [dbo].[Sections] p ON p.[Id]=r.[SectionId] WHERE r.[SectionId] IS NOT NULL AND r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Courses.SectionId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Subjects] p WHERE p.[Id]=r.[SubjectId]))
        THROW 51000, 'Cannot normalize Courses.SubjectId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Courses] r JOIN [dbo].[Subjects] p ON p.[Id]=r.[SubjectId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Courses.SubjectId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_Courses_AcademicYearId] ON [dbo].[Courses]([AcademicYearId]);
    CREATE INDEX [IX_Courses_ClassId] ON [dbo].[Courses]([ClassId]);
    CREATE INDEX [IX_Courses_SectionId] ON [dbo].[Courses]([SectionId]);
    CREATE INDEX [IX_Courses_SubjectId] ON [dbo].[Courses]([SubjectId]);
    ALTER TABLE [dbo].[Courses] WITH CHECK ADD CONSTRAINT [FK_Courses_AcademicYears_AcademicYearId] FOREIGN KEY([AcademicYearId]) REFERENCES [dbo].[AcademicYears]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Courses] WITH CHECK ADD CONSTRAINT [FK_Courses_Classes_ClassId] FOREIGN KEY([ClassId]) REFERENCES [dbo].[Classes]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Courses] WITH CHECK ADD CONSTRAINT [FK_Courses_Sections_SectionId] FOREIGN KEY([SectionId]) REFERENCES [dbo].[Sections]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Courses] WITH CHECK ADD CONSTRAINT [FK_Courses_Subjects_SubjectId] FOREIGN KEY([SubjectId]) REFERENCES [dbo].[Subjects]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
