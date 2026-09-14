using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913065500_NormalizePromotionRelations")]
public partial class NormalizePromotionRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRequiredRelation(migrationBuilder, "StudentId", "Students");
        NormalizeRequiredRelation(migrationBuilder, "FromClassId", "Classes");
        NormalizeRequiredRelation(migrationBuilder, "ToClassId", "Classes");
        NormalizeRequiredRelation(migrationBuilder, "FromYearId", "AcademicYears");
        NormalizeRequiredRelation(migrationBuilder, "ToYearId", "AcademicYears");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRequiredRelation(MigrationBuilder migrationBuilder, string column, string parentTable)
    {
        var shadow = column + "1";
        var normalized = column + "Normalized";
        var indexName = $"IX_Promotions_{column}";
        var fkName = $"FK_Promotions_{parentTable}_{column}";

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[Promotions]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Promotions.{column} because {parentTable} does not exist.', 1;
    IF COL_LENGTH('dbo.Promotions','{column}') IS NULL
        THROW 51000, 'Cannot normalize Promotions.{column} because the canonical column does not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[Promotions] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Promotions]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[Promotions];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Promotions]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.Promotions','{shadow}') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Promotions] WHERE [{shadow}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadow}]))
            THROW 51000, 'Cannot normalize Promotions.{column} because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.Promotions','{normalized}') IS NOT NULL
            ALTER TABLE [dbo].[Promotions] DROP COLUMN [{normalized}];
        ALTER TABLE [dbo].[Promotions] ADD [{normalized}] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Promotions] SET [{normalized}]=COALESCE(CONVERT(bigint,[{shadow}]),NULLIF(CONVERT(bigint,[{column}]),0));');
        IF EXISTS(SELECT 1 FROM [dbo].[Promotions] WHERE [{normalized}] IS NULL)
            THROW 51000, 'Cannot normalize Promotions.{column} because required references are null.', 1;
        ALTER TABLE [dbo].[Promotions] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[Promotions] DROP COLUMN [{shadow}];
        EXEC sp_rename N'dbo.Promotions.{normalized}', N'{column}', 'COLUMN';
        ALTER TABLE [dbo].[Promotions] ALTER COLUMN [{column}] BIGINT NOT NULL;
    END
    ELSE
        ALTER TABLE [dbo].[Promotions] ALTER COLUMN [{column}] BIGINT NOT NULL;

    IF EXISTS(SELECT 1 FROM [dbo].[Promotions] r WHERE NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize Promotions.{column} because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Promotions] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Promotions.{column} because cross-tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[Promotions]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[Promotions]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[Promotions]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[Promotions] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
