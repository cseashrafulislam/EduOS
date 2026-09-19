using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913035500_NormalizeEnrollmentRelations")]
public partial class NormalizeEnrollmentRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Enrollments]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[AcademicYears]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Classes]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Sections]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[Groups]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Enrollments because one or more referenced tables do not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[Enrollments] DROP CONSTRAINT ['+f.name+N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Enrollments]') AND c.name IN (N'AcademicYearId',N'AcademicYearId1',N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'GroupId',N'GroupId1');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[Enrollments];'
    FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Enrollments]') AND i.index_id>0 AND i.is_primary_key=0 AND i.is_unique_constraint=0
      AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND c.name IN (N'AcademicYearId',N'AcademicYearId1',N'ClassId',N'ClassId1',N'SectionId',N'SectionId1',N'GroupId',N'GroupId1'));
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.Enrollments','AcademicYearId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [AcademicYearId1] IS NOT NULL AND [AcademicYearId] IS NOT NULL AND CONVERT(bigint,[AcademicYearId])<>CONVERT(bigint,[AcademicYearId1])) THROW 51000, 'Cannot normalize Enrollments.AcademicYearId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Enrollments] ADD [AcademicYearIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Enrollments] SET [AcademicYearIdNormalized]=COALESCE(CONVERT(bigint,[AcademicYearId1]),CONVERT(bigint,[AcademicYearId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [AcademicYearIdNormalized] IS NULL) THROW 51000, 'Cannot normalize Enrollments.AcademicYearId because required references are null.', 1;
        ALTER TABLE [dbo].[Enrollments] DROP COLUMN [AcademicYearId]; ALTER TABLE [dbo].[Enrollments] DROP COLUMN [AcademicYearId1]; EXEC sp_rename N'dbo.Enrollments.AcademicYearIdNormalized',N'AcademicYearId','COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Enrollments] ALTER COLUMN [AcademicYearId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Enrollments','ClassId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [ClassId1] IS NOT NULL AND [ClassId] IS NOT NULL AND CONVERT(bigint,[ClassId])<>CONVERT(bigint,[ClassId1])) THROW 51000, 'Cannot normalize Enrollments.ClassId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Enrollments] ADD [ClassIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Enrollments] SET [ClassIdNormalized]=COALESCE(CONVERT(bigint,[ClassId1]),CONVERT(bigint,[ClassId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [ClassIdNormalized] IS NULL) THROW 51000, 'Cannot normalize Enrollments.ClassId because required references are null.', 1;
        ALTER TABLE [dbo].[Enrollments] DROP COLUMN [ClassId]; ALTER TABLE [dbo].[Enrollments] DROP COLUMN [ClassId1]; EXEC sp_rename N'dbo.Enrollments.ClassIdNormalized',N'ClassId','COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Enrollments] ALTER COLUMN [ClassId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Enrollments','SectionId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [SectionId1] IS NOT NULL AND [SectionId] IS NOT NULL AND CONVERT(bigint,[SectionId])<>CONVERT(bigint,[SectionId1])) THROW 51000, 'Cannot normalize Enrollments.SectionId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Enrollments] ADD [SectionIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Enrollments] SET [SectionIdNormalized]=COALESCE(CONVERT(bigint,[SectionId1]),CONVERT(bigint,[SectionId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [SectionIdNormalized] IS NULL) THROW 51000, 'Cannot normalize Enrollments.SectionId because required references are null.', 1;
        ALTER TABLE [dbo].[Enrollments] DROP COLUMN [SectionId]; ALTER TABLE [dbo].[Enrollments] DROP COLUMN [SectionId1]; EXEC sp_rename N'dbo.Enrollments.SectionIdNormalized',N'SectionId','COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Enrollments] ALTER COLUMN [SectionId] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.Enrollments','GroupId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] WHERE [GroupId1] IS NOT NULL AND [GroupId] IS NOT NULL AND CONVERT(bigint,[GroupId])<>CONVERT(bigint,[GroupId1])) THROW 51000, 'Cannot normalize Enrollments.GroupId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[Enrollments] ADD [GroupIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Enrollments] SET [GroupIdNormalized]=COALESCE(CONVERT(bigint,[GroupId1]),CONVERT(bigint,[GroupId]));');
        ALTER TABLE [dbo].[Enrollments] DROP COLUMN [GroupId]; ALTER TABLE [dbo].[Enrollments] DROP COLUMN [GroupId1]; EXEC sp_rename N'dbo.Enrollments.GroupIdNormalized',N'GroupId','COLUMN';
    END
    ELSE ALTER TABLE [dbo].[Enrollments] ALTER COLUMN [GroupId] BIGINT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[AcademicYears] p WHERE p.Id=r.AcademicYearId)) THROW 51000, 'Cannot normalize Enrollments.AcademicYearId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r JOIN [dbo].[AcademicYears] p ON p.Id=r.AcademicYearId WHERE r.TenantId<>p.TenantId) THROW 51000, 'Cannot normalize Enrollments.AcademicYearId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Classes] p WHERE p.Id=r.ClassId)) THROW 51000, 'Cannot normalize Enrollments.ClassId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r JOIN [dbo].[Classes] p ON p.Id=r.ClassId WHERE r.TenantId<>p.TenantId) THROW 51000, 'Cannot normalize Enrollments.ClassId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Sections] p WHERE p.Id=r.SectionId)) THROW 51000, 'Cannot normalize Enrollments.SectionId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r JOIN [dbo].[Sections] p ON p.Id=r.SectionId WHERE r.TenantId<>p.TenantId) THROW 51000, 'Cannot normalize Enrollments.SectionId because cross-tenant references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r WHERE r.GroupId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[Groups] p WHERE p.Id=r.GroupId)) THROW 51000, 'Cannot normalize Enrollments.GroupId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Enrollments] r JOIN [dbo].[Groups] p ON p.Id=r.GroupId WHERE r.GroupId IS NOT NULL AND r.TenantId<>p.TenantId) THROW 51000, 'Cannot normalize Enrollments.GroupId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_Enrollments_AcademicYearId] ON [dbo].[Enrollments]([AcademicYearId]);
    CREATE INDEX [IX_Enrollments_ClassId] ON [dbo].[Enrollments]([ClassId]);
    CREATE INDEX [IX_Enrollments_SectionId] ON [dbo].[Enrollments]([SectionId]);
    CREATE INDEX [IX_Enrollments_GroupId] ON [dbo].[Enrollments]([GroupId]);
    CREATE UNIQUE INDEX [IX_Enrollments_TenantId_StudentId_AcademicYearId] ON [dbo].[Enrollments]([TenantId],[StudentId],[AcademicYearId]) WHERE [IsDeleted]=0;
    ALTER TABLE [dbo].[Enrollments] WITH CHECK ADD CONSTRAINT [FK_Enrollments_AcademicYears_AcademicYearId] FOREIGN KEY([AcademicYearId]) REFERENCES [dbo].[AcademicYears]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Enrollments] WITH CHECK ADD CONSTRAINT [FK_Enrollments_Classes_ClassId] FOREIGN KEY([ClassId]) REFERENCES [dbo].[Classes]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Enrollments] WITH CHECK ADD CONSTRAINT [FK_Enrollments_Sections_SectionId] FOREIGN KEY([SectionId]) REFERENCES [dbo].[Sections]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [dbo].[Enrollments] WITH CHECK ADD CONSTRAINT [FK_Enrollments_Groups_GroupId] FOREIGN KEY([GroupId]) REFERENCES [dbo].[Groups]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
