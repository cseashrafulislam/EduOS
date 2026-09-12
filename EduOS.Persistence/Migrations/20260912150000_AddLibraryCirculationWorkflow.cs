using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912150000_AddLibraryCirculationWorkflow")]
public partial class AddLibraryCirculationWorkflow : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        AlterFk(m, "BookIssues", "BookId", "Books", false);
        AlterFk(m, "BookIssues", "StudentId", "Students", true);
        AlterFk(m, "BookIssues", "EmployeeId", "Employees", true);
        m.AddColumn<Guid>(name: "PublicId", table: "Books", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()");
        m.AddColumn<bool>(name: "IsActive", table: "Books", type: "bit", nullable: false, defaultValue: true);
        m.AddColumn<byte[]>(name: "RowVersion", table: "Books", type: "rowversion", rowVersion: true, nullable: false);
        m.AddColumn<Guid>(name: "PublicId", table: "BookIssues", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()");
        m.AddColumn<Guid>(name: "ClientRequestId", table: "BookIssues", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()");
        m.AddColumn<long>(name: "IssuedByUserId", table: "BookIssues", type: "bigint", nullable: true);
        m.AddColumn<long>(name: "ReturnedByUserId", table: "BookIssues", type: "bigint", nullable: true);
        m.AddColumn<byte[]>(name: "RowVersion", table: "BookIssues", type: "rowversion", rowVersion: true, nullable: false);
        m.CreateIndex(name: "UX_Books_Tenant_PublicId", table: "Books", columns: new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex(name: "UX_BookIssues_Tenant_PublicId", table: "BookIssues", columns: new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex(name: "UX_BookIssues_Tenant_ClientRequestId", table: "BookIssues", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder m) { }

    private static void AlterFk(MigrationBuilder m, string table, string column, string principal, bool nullable)
    {
        var nullSql = nullable ? "NULL" : "NOT NULL";
        m.Sql($@"DECLARE @sql nvarchar(max)=N'';
SELECT @sql+=N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT ['+fk.name+N'];' FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id WHERE fkc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}';
IF LEN(@sql)>0 EXEC sp_executesql @sql;
DECLARE @idx nvarchar(max)=N'';
SELECT @idx+=N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}' AND i.is_primary_key=0 AND i.is_unique_constraint=0;
IF LEN(@idx)>0 EXEC sp_executesql @idx;
ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] bigint {nullSql};
CREATE INDEX [IX_{table}_{column}] ON [dbo].[{table}]([{column}]);
ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [FK_{table}_{principal}_{column}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{principal}]([Id]);");
    }
}
