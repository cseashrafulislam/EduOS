using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912080000_AddSafeStudentFeeBilling")]
public partial class AddSafeStudentFeeBilling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        NormalizeRequiredKey(migrationBuilder, "StudentInvoices", "StudentId", "Students");
        NormalizeRequiredKey(migrationBuilder, "InvoiceItems", "InvoiceId", "StudentInvoices");
        NormalizeRequiredKey(migrationBuilder, "InvoiceItems", "FeeHeadId", "FeeHeads");
        NormalizeRequiredKey(migrationBuilder, "Payments", "InvoiceId", "StudentInvoices");
        NormalizeRequiredKey(migrationBuilder, "Payments", "StudentId", "Students");
        NormalizeNullableKey(migrationBuilder, "Payments", "BankAccountId", "BankAccounts");
        NormalizeRequiredKey(migrationBuilder, "StudentDiscounts", "StudentId", "Students");
        NormalizeRequiredKey(migrationBuilder, "StudentDiscounts", "DiscountId", "Discounts");
        NormalizeRequiredKey(migrationBuilder, "FeeStructures", "FeeHeadId", "FeeHeads");
        NormalizeNullableKey(migrationBuilder, "Discounts", "FeeHeadId", "FeeHeads");
        migrationBuilder.Sql("IF COL_LENGTH('dbo.Payments','ReceivedBy') IS NOT NULL ALTER TABLE [dbo].[Payments] ALTER COLUMN [ReceivedBy] BIGINT NOT NULL;");
        migrationBuilder.Sql("IF COL_LENGTH('dbo.StudentDiscounts','ApprovedBy') IS NOT NULL ALTER TABLE [dbo].[StudentDiscounts] ALTER COLUMN [ApprovedBy] BIGINT NOT NULL;");

        migrationBuilder.AddColumn<Guid>(name:"PublicId", table:"StudentInvoices", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.AddColumn<Guid>(name:"GenerationRequestId", table:"StudentInvoices", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.AddColumn<string>(name:"BillingKey", table:"StudentInvoices", type:"nvarchar(120)", maxLength:120, nullable:false, defaultValue:"");
        migrationBuilder.AddColumn<int>(name:"AcademicYearId", table:"StudentInvoices", type:"int", nullable:false, defaultValue:0);
        migrationBuilder.AddColumn<int>(name:"ClassId", table:"StudentInvoices", type:"int", nullable:false, defaultValue:0);
        migrationBuilder.AddColumn<int>(name:"SectionId", table:"StudentInvoices", type:"int", nullable:false, defaultValue:0);
        migrationBuilder.AddColumn<byte[]>(name:"RowVersion", table:"StudentInvoices", type:"rowversion", rowVersion:true, nullable:false);
        migrationBuilder.Sql("UPDATE i SET BillingKey='LEGACY-'+CONVERT(varchar(30),i.Id), AcademicYearId=ISNULL(s.AcademicYearId,0), ClassId=ISNULL(s.ClassId,0), SectionId=ISNULL(s.SectionId,0) FROM StudentInvoices i LEFT JOIN Students s ON s.Id=i.StudentId WHERE i.BillingKey='';");

        migrationBuilder.AddColumn<Guid>(name:"PublicId", table:"Payments", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.AddColumn<Guid>(name:"ClientRequestId", table:"Payments", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.CreateIndex(name:"UX_StudentInvoices_Tenant_PublicId", table:"StudentInvoices", columns:new[]{"TenantId","PublicId"}, unique:true);
        migrationBuilder.CreateIndex(name:"UX_StudentInvoices_Tenant_BillingKey", table:"StudentInvoices", columns:new[]{"TenantId","BillingKey"}, unique:true, filter:"[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name:"UX_StudentInvoices_Tenant_Request_Student", table:"StudentInvoices", columns:new[]{"TenantId","GenerationRequestId","StudentId"}, unique:true, filter:"[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name:"UX_Payments_Tenant_PublicId", table:"Payments", columns:new[]{"TenantId","PublicId"}, unique:true);
        migrationBuilder.CreateIndex(name:"UX_Payments_Tenant_ClientRequestId", table:"Payments", columns:new[]{"TenantId","ClientRequestId"}, unique:true, filter:"[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }

    private static void NormalizeRequiredKey(MigrationBuilder migrationBuilder, string table, string column, string principal)
    {
        migrationBuilder.Sql($@"DECLARE @sql nvarchar(max)=N''; SELECT @sql += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT ['+fk.name+N'];' FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id WHERE fkc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}'; IF LEN(@sql)>0 EXEC sp_executesql @sql; DECLARE @ix nvarchar(max)=N''; SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}' AND i.is_primary_key=0 AND i.is_unique_constraint=0; IF LEN(@ix)>0 EXEC sp_executesql @ix; ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NOT NULL; CREATE INDEX [IX_{table}_{column}] ON [dbo].[{table}]([{column}]); ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [FK_{table}_{principal}_{column}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{principal}]([Id]);");
    }
    private static void NormalizeNullableKey(MigrationBuilder migrationBuilder, string table, string column, string principal)
    {
        migrationBuilder.Sql($@"DECLARE @sql nvarchar(max)=N''; SELECT @sql += N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT ['+fk.name+N'];' FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id WHERE fkc.parent_object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}'; IF LEN(@sql)>0 EXEC sp_executesql @sql; DECLARE @ix nvarchar(max)=N''; SELECT @ix += N'DROP INDEX ['+i.name+N'] ON [dbo].[{table}];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[{table}]') AND c.name=N'{column}' AND i.is_primary_key=0 AND i.is_unique_constraint=0; IF LEN(@ix)>0 EXEC sp_executesql @ix; ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] BIGINT NULL; CREATE INDEX [IX_{table}_{column}] ON [dbo].[{table}]([{column}]); ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [FK_{table}_{principal}_{column}] FOREIGN KEY([{column}]) REFERENCES [dbo].[{principal}]([Id]);");
    }
}
