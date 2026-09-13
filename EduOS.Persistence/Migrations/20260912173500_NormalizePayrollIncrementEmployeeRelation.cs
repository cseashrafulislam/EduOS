using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912173500_NormalizePayrollIncrementEmployeeRelation")]
public partial class NormalizePayrollIncrementEmployeeRelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[Increments]', N'U') IS NOT NULL AND COL_LENGTH('dbo.Increments','EmployeeId') IS NOT NULL
BEGIN
    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[Increments] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Increments]') AND c.name IN (N'EmployeeId',N'EmployeeId1');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[Increments];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Increments]') AND c.name IN (N'EmployeeId',N'EmployeeId1') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.Increments','EmployeeId1') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[Increments] ADD [EmployeeIdNormalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Increments] SET [EmployeeIdNormalized]=COALESCE(CONVERT(bigint,[EmployeeId]),CONVERT(bigint,[EmployeeId1]));');
        ALTER TABLE [dbo].[Increments] DROP COLUMN [EmployeeId];
        ALTER TABLE [dbo].[Increments] DROP COLUMN [EmployeeId1];
        EXEC sp_rename N'dbo.Increments.EmployeeIdNormalized', N'EmployeeId', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[Increments] ALTER COLUMN [EmployeeId] BIGINT NOT NULL;

    IF EXISTS(
        SELECT 1 FROM [dbo].[Increments] i
        WHERE i.[EmployeeId] IS NULL OR NOT EXISTS(SELECT 1 FROM [dbo].[Employees] e WHERE e.Id=i.[EmployeeId])
    )
        THROW 51000, 'Cannot normalize Increments.EmployeeId because orphaned employee references exist.', 1;

    ALTER TABLE [dbo].[Increments] ALTER COLUMN [EmployeeId] BIGINT NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[Increments]') AND name=N'IX_Increments_EmployeeId')
        CREATE INDEX [IX_Increments_EmployeeId] ON [dbo].[Increments]([EmployeeId]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[Increments]') AND name=N'FK_Increments_Employees_EmployeeId')
        ALTER TABLE [dbo].[Increments] WITH CHECK ADD CONSTRAINT [FK_Increments_Employees_EmployeeId] FOREIGN KEY([EmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }
}
