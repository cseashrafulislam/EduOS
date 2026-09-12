using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912170000_NormalizeLegacyHrAndIdCardRelations")]
public partial class NormalizeLegacyHrAndIdCardRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeScalarKey(migrationBuilder, "HREmployees", "DepartmentId", true);
        NormalizeScalarKey(migrationBuilder, "HREmployees", "DesignationId", true);
        NormalizeScalarKey(migrationBuilder, "HRAttendanceLogs", "EmployeeId", false);
        NormalizeScalarKey(migrationBuilder, "HRPayrolls", "EmployeeId", false);
        NormalizeScalarKey(migrationBuilder, "HRSalaryStructures", "EmployeeId", false);
        NormalizeScalarKey(migrationBuilder, "HRLeaveApplications", "EmployeeId", false);
        NormalizeScalarKey(migrationBuilder, "HRLeaveApplications", "LeaveTypeId", false);

        NormalizeOptionalReference(migrationBuilder, "IdCards", "StudentId", "Students");
        NormalizeOptionalReference(migrationBuilder, "IdCards", "EmployeeId", "Employees");
        NormalizeOptionalReference(migrationBuilder, "IdCards", "TemplateId", "DocumentTemplates");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Key widening is intentionally irreversible: converting bigint identifiers back
        // to int could truncate production data after this migration has been applied.
    }

    private static void NormalizeScalarKey(MigrationBuilder migrationBuilder, string table, string column, bool nullable)
    {
        var nullSql = nullable ? "NULL" : "NOT NULL";
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL AND COL_LENGTH('dbo.{table}','{column}') IS NOT NULL
BEGIN
    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}';
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}' AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT {nullSql};
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'IX_{table}_{column}')
        CREATE INDEX [IX_{table}_{column}] ON [dbo].[{table}]([{column}]);
END;");
    }

    private static void NormalizeOptionalReference(MigrationBuilder migrationBuilder, string table, string column, string principalTable)
    {
        migrationBuilder.Sql($@"
IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NOT NULL AND COL_LENGTH('dbo.{table}','{column}') IS NOT NULL
BEGIN
    DECLARE @fk nvarchar(max)=N'';
    SELECT @fk += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT [' + f.name + N'];'
    FROM sys.foreign_keys f
    JOIN sys.foreign_key_columns fc ON f.object_id=fc.constraint_object_id
    JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
    WHERE fc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{column}1');
    IF LEN(@fk)>0 EXEC sp_executesql @fk;

    DECLARE @ix nvarchar(max)=N'';
    SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];'
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name IN (N'{column}',N'{column}1') AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF LEN(@ix)>0 EXEC sp_executesql @ix;

    IF COL_LENGTH('dbo.{table}','{column}1') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[{table}] ADD [{column}Normalized] BIGINT NULL;
        EXEC(N'UPDATE [dbo].[{table}] SET [{column}Normalized]=COALESCE(CONVERT(bigint,[{column}]),CONVERT(bigint,[{column}1]));');
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}];
        ALTER TABLE [dbo].[{table}] DROP COLUMN [{column}1];
        EXEC sp_rename N'dbo.{table}.{column}Normalized', N'{column}', 'COLUMN';
    END
    ELSE
        ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NULL;

    EXEC(N'UPDATE x SET [{column}]=NULL FROM [dbo].[{table}] x WHERE [{column}] IS NOT NULL AND NOT EXISTS(SELECT 1 FROM [dbo].[{principalTable}] p WHERE p.Id=x.[{column}]);');

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'IX_{table}_{column}')
        CREATE INDEX [IX_{table}_{column}] ON [dbo].[{table}]([{column}]);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND name=N'FK_{table}_{principalTable}_{column}')
        ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [FK_{table}_{principalTable}_{column}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{principalTable}]([Id]);
END;");
    }
}
