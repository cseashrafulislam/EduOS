using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913103000_NormalizeEmployeeRelations")]
public partial class NormalizeEmployeeRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRelation(migrationBuilder, "DesignationId", "Designations", false);
        NormalizeRelation(migrationBuilder, "DepartmentId", "Departments", true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identifier widening is intentionally irreversible to avoid truncating production keys.
    }

    private static void NormalizeRelation(MigrationBuilder migrationBuilder, string column, string parentTable, bool nullable)
    {
        var shadow = column + "1";
        var normalized = column + "Normalized";
        var nullSql = nullable ? "NULL" : "NOT NULL";
        var indexName = $"IX_Employees_{column}";
        var fkName = $"FK_Employees_{parentTable}_{column}";

        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[Employees]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[{parentTable}]', N'U') IS NULL
        THROW 51000, 'Cannot normalize Employees.{column} because {parentTable} does not exist.', 1;
    IF COL_LENGTH('dbo.Employees','{column}') IS NULL
        THROW 51000, 'Cannot normalize Employees.{column} because the canonical column does not exist.', 1;

    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[Employees] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[Employees]') AND c.name IN (N'{column}',N'{shadow}');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[Employees];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[Employees]') AND c.name IN (N'{column}',N'{shadow}')
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.Employees','{shadow}') IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM [dbo].[Employees] WHERE [{shadow}] IS NOT NULL AND [{column}] IS NOT NULL AND CONVERT(bigint,[{column}])<>0 AND CONVERT(bigint,[{column}])<>CONVERT(bigint,[{shadow}]))
            THROW 51000, 'Cannot normalize Employees.{column} because canonical and shadow references conflict.', 1;
        IF COL_LENGTH('dbo.Employees','{normalized}') IS NOT NULL
            ALTER TABLE [dbo].[Employees] DROP COLUMN [{normalized}];
        ALTER TABLE [dbo].[Employees] ADD [{normalized}] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[Employees] SET [{normalized}]=COALESCE(CONVERT(bigint,[{shadow}]),NULLIF(CONVERT(bigint,[{column}]),0));');
        {(nullable ? "" : $"IF EXISTS(SELECT 1 FROM [dbo].[Employees] WHERE [{normalized}] IS NULL) THROW 51000, 'Cannot normalize Employees.{column} because required references are null.', 1;")}
        ALTER TABLE [dbo].[Employees] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[Employees] DROP COLUMN [{shadow}];
        EXEC sp_rename N'dbo.Employees.{normalized}', N'{column}', 'COLUMN';
        ALTER TABLE [dbo].[Employees] ALTER COLUMN [{column}] BIGINT {nullSql};
    END
    ELSE
        ALTER TABLE [dbo].[Employees] ALTER COLUMN [{column}] BIGINT {nullSql};

    IF EXISTS(SELECT 1 FROM [dbo].[Employees] r WHERE r.[{column}] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[{parentTable}] p WHERE p.[Id]=r.[{column}]))
        THROW 51000, 'Cannot normalize Employees.{column} because orphaned references exist.', 1;
    IF EXISTS(SELECT 1 FROM [dbo].[Employees] r JOIN [dbo].[{parentTable}] p ON p.[Id]=r.[{column}] WHERE r.[{column}] IS NOT NULL AND r.[TenantId]<>p.[TenantId])
        THROW 51000, 'Cannot normalize Employees.{column} because cross-tenant references exist.', 1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[Employees]') AND name=N'{indexName}')
        CREATE INDEX [{indexName}] ON [dbo].[Employees]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[Employees]') AND name=N'{fkName}')
        ALTER TABLE [dbo].[Employees] WITH CHECK ADD CONSTRAINT [{fkName}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{parentTable}]([Id]) ON DELETE NO ACTION;
END;");
    }
}
