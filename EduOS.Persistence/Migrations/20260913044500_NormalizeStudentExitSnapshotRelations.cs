using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913044500_NormalizeStudentExitSnapshotRelations")]
public partial class NormalizeStudentExitSnapshotRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[StudentExitRecords]', N'U') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[StudentExitRecords] ALTER COLUMN [AcademicYearId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[StudentExitRecords] ALTER COLUMN [ClassId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[StudentExitRecords] ALTER COLUMN [SectionId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[StudentExitRecords] ALTER COLUMN [GroupId] BIGINT NULL;
END;

IF OBJECT_ID(N'[dbo].[TransferCertificates]', N'U') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[TransferCertificates] ALTER COLUMN [LastSectionId] BIGINT NOT NULL;
    ALTER TABLE [dbo].[TransferCertificates] ALTER COLUMN [LastAcademicYearId] BIGINT NOT NULL;

    IF OBJECT_ID(N'[dbo].[Classes]', N'U') IS NULL
        THROW 51000, 'Cannot normalize TransferCertificates.LastClassId because Classes does not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[TransferCertificates] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[TransferCertificates]') AND c.name IN (N'LastClassId',N'LastClassId1');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[TransferCertificates];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[TransferCertificates]') AND c.name IN (N'LastClassId',N'LastClassId1')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.TransferCertificates','LastClassId1') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[TransferCertificates] WHERE [LastClassId1] IS NOT NULL AND [LastClassId] IS NOT NULL AND CONVERT(bigint,[LastClassId])<>CONVERT(bigint,[LastClassId1]))
            THROW 51000, 'Cannot normalize TransferCertificates.LastClassId because canonical and shadow references conflict.', 1;
        ALTER TABLE [dbo].[TransferCertificates] ADD [LastClassIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[TransferCertificates] SET [LastClassIdNormalized]=COALESCE(CONVERT(bigint,[LastClassId1]),CONVERT(bigint,[LastClassId]));');
        IF EXISTS(SELECT 1 FROM [dbo].[TransferCertificates] WHERE [LastClassIdNormalized] IS NULL)
            THROW 51000, 'Cannot normalize TransferCertificates.LastClassId because required references are null.', 1;
        ALTER TABLE [dbo].[TransferCertificates] DROP COLUMN [LastClassId];
        ALTER TABLE [dbo].[TransferCertificates] DROP COLUMN [LastClassId1];
        EXEC sp_rename N'dbo.TransferCertificates.LastClassIdNormalized', N'LastClassId', 'COLUMN';
    END
    ELSE ALTER TABLE [dbo].[TransferCertificates] ALTER COLUMN [LastClassId] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[TransferCertificates] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[Classes] p WHERE p.[Id]=r.[LastClassId]))
        THROW 51000, 'Cannot normalize TransferCertificates.LastClassId because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[TransferCertificates] r JOIN [dbo].[Classes] p ON p.[Id]=r.[LastClassId] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize TransferCertificates.LastClassId because cross-tenant references exist.', 1;

    CREATE INDEX [IX_TransferCertificates_LastClassId] ON [dbo].[TransferCertificates]([LastClassId]);
    ALTER TABLE [dbo].[TransferCertificates] WITH CHECK ADD CONSTRAINT [FK_TransferCertificates_Classes_LastClassId] FOREIGN KEY([LastClassId]) REFERENCES [dbo].[Classes]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
