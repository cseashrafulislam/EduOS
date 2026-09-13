using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912225500_NormalizeSectionSubjectRelations")]
public partial class NormalizeSectionSubjectRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRelation(migrationBuilder, "Sections", "ClassId", "ClassId1", "Classes", false);
        NormalizeRelation(migrationBuilder, "Subjects", "ClassId", "ClassId1", "Classes", false);
        NormalizeRelation(migrationBuilder, "Subjects", "GroupId", "GroupId1", "Groups", true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRelation(MigrationBuilder migrationBuilder, string table, string column, string shadowColumn, string parentTable, bool nullable)
    {
        var indexName = $"IX_{table}_{column}";
        var fkName = $"FK_{table}_{parentTable}_{column}";
        var nullSql = nullable ? "NULL" : "NOT NULL";
        var requiredCheck = nullable ? $"r.[{column}] IS NOT NULL AND " : $"r.[{column}] IS NULL OR r.[{column}]=0 OR ";
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL AND COL_LENGTH('dbo.{table}','{column}') IS NOT NULL
BEGIN
    DECLARE @fk_{table}_{column} nvarchar(max)=N'';
    SELECT @fk_{table}_{column} += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadowColumn}');
    IF LEN(@fk_{table}_{column})>0 EXEC sp_executesql @fk_{table}_{column};

    DECLARE @ix_{table}_{column} nvarchar(max)=N'';
    SELECT @ix_{table}_{column} += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{shadowColumn}') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix_{table}_{column})>0 EXEC sp_executesql @ix_{table}_{column};

    IF COL_LENGTH('dbo.{table}','{shadowColumn}') IS NOT NULL
    BEGIN
        IF EXISTS(
            SELECT 1 FROM [dbo].[{table}]
            WHERE [{shadowColumn}] IS NOT NULL AND [{column}] IS NOT NULL AND [{column}]<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadowColumn}])
        ) THROW 51000, 'Cannot normalize {table}.{column} because canonical and shadow references conflict.', 1;

        ALTER TABLE [dbo].[{table}] ADD [{column}Normalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[{table}] SET [{column}Normalized]=CASE WHEN [{shadowColumn}] IS NOT NULL AND ([{column}] IS NULL OR [{column}]=0) THEN CONVERT(bigint,[{shadowColumn}]) ELSE CONVERT(bigint,[{column}]) END;');
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{shadowColumn}];
        EXEC sp_rename N'dbo.{table}.{column}Normalized', N'{column}', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT {nullSql};

    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize {table}.{column} because parent table {parentTable} does not exist.', 1;

    IF EXISTS(
        SELECT 1 FROM [dbo].[{table}] r
        WHERE {requiredCheck}NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}])
    ) THROW 51000, 'Cannot normalize {table}.{column} because orphaned references exist.', 1;

    IF COL_LENGTH('dbo.{table}','TenantId') IS NOT NULL AND COL_LENGTH('dbo.{parentTable}','TenantId') IS NOT NULL AND EXISTS(
        SELECT 1 FROM [dbo].[{table}] r
        JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}]
        WHERE r.[TenantId]<>p.[TenantId]
    ) THROW 51000, 'Cannot normalize {table}.{column} because cross-tenant references exist.', 1;

    ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT {nullSql};
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[{table}]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
