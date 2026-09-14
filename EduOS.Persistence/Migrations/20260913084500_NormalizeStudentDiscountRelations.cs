using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913084500_NormalizeStudentDiscountRelations")]
public partial class NormalizeStudentDiscountRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRequiredRelation(migrationBuilder, "StudentId", "Students");
        NormalizeRequiredRelation(migrationBuilder, "DiscountId", "Discounts");

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[StudentDiscounts]', N'U') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[StudentDiscounts] ALTER COLUMN [ApprovedBy] BIGINT NOT NULL;

    IF COL_LENGTH('dbo.AspNetUsers','Id') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[StudentDiscounts] r
        WHERE NOT EXISTS(SELECT 1 FROM [dbo].[AspNetUsers] u WHERE u.[Id]=r.[ApprovedBy])
    ) THROW 51000, 'Cannot normalize StudentDiscounts.ApprovedBy because orphaned user references exist.', 1;

    IF COL_LENGTH('dbo.AspNetUsers','TenantId') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[StudentDiscounts] r
        JOIN [dbo].[AspNetUsers] u ON u.[Id]=r.[ApprovedBy]
        WHERE u.[TenantId] IS NOT NULL AND u.[TenantId]<>r.[TenantId]
    ) THROW 51000, 'Cannot normalize StudentDiscounts.ApprovedBy because cross-tenant approver references exist.', 1;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRequiredRelation(MigrationBuilder migrationBuilder, string column, string parentTable)
    {
        var shadow = column + "1";
        var normalized = column + "Normalized";
        var indexName = $"IX_StudentDiscounts_{column}";
        var fkName = $"FK_StudentDiscounts_{parentTable}_{column}";

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[StudentDiscounts]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize StudentDiscounts.{column} because {parentTable} does not exist.', 1;
    IF COL_LENGTH('dbo.StudentDiscounts','{column}') IS NULL
        THROW 51000, 'Cannot normalize StudentDiscounts.{column} because the canonical column does not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[StudentDiscounts] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[StudentDiscounts]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[StudentDiscounts];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[StudentDiscounts]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.StudentDiscounts','{shadow}') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[StudentDiscounts] WHERE [{shadow}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadow}]))
            THROW 51000, 'Cannot normalize StudentDiscounts.{column} because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.StudentDiscounts','{normalized}') IS NOT NULL
            ALTER TABLE [dbo].[StudentDiscounts] DROP COLUMN [{normalized}];
        ALTER TABLE [dbo].[StudentDiscounts] ADD [{normalized}] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[StudentDiscounts] SET [{normalized}]=COALESCE(CONVERT(bigint,[{shadow}]),NULLIF(CONVERT(bigint,[{column}]),0));');
        IF EXISTS(SELECT 1 FROM [dbo].[StudentDiscounts] WHERE [{normalized}] IS NULL)
            THROW 51000, 'Cannot normalize StudentDiscounts.{column} because required references are null.', 1;
        ALTER TABLE [dbo].[StudentDiscounts] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[StudentDiscounts] DROP COLUMN [{shadow}];
        EXEC sp_rename N'dbo.StudentDiscounts.{normalized}', N'{column}', 'COLUMN';
        ALTER TABLE [dbo].[StudentDiscounts] ALTER COLUMN [{column}] BIGINT NOT NULL;
    END
    ELSE
        ALTER TABLE [dbo].[StudentDiscounts] ALTER COLUMN [{column}] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[StudentDiscounts] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize StudentDiscounts.{column} because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[StudentDiscounts] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize StudentDiscounts.{column} because cross-tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[StudentDiscounts]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[StudentDiscounts]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[StudentDiscounts]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[StudentDiscounts] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
