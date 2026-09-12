using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260912100000_AddStudentExitLifecycle")]
public partial class AddStudentExitLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DECLARE @sql nvarchar(max)=N''; SELECT @sql+=N'ALTER TABLE [dbo].[TransferCertificates] DROP CONSTRAINT ['+fk.name+N'];' FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id WHERE fkc.parent_object_id=OBJECT_ID(N'[dbo].[TransferCertificates]') AND c.name=N'StudentId'; IF LEN(@sql)>0 EXEC sp_executesql @sql; DECLARE @ix nvarchar(max)=N''; SELECT @ix+=N'DROP INDEX ['+i.name+N'] ON [dbo].[TransferCertificates];' FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'[dbo].[TransferCertificates]') AND c.name=N'StudentId' AND i.is_primary_key=0 AND i.is_unique_constraint=0; IF LEN(@ix)>0 EXEC sp_executesql @ix; ALTER TABLE [dbo].[TransferCertificates] ALTER COLUMN [StudentId] BIGINT NOT NULL; CREATE INDEX [IX_TransferCertificates_StudentId] ON [dbo].[TransferCertificates]([StudentId]); ALTER TABLE [dbo].[TransferCertificates] WITH CHECK ADD CONSTRAINT [FK_TransferCertificates_Students_StudentId] FOREIGN KEY([StudentId]) REFERENCES [dbo].[Students]([Id]);");
        migrationBuilder.Sql("IF COL_LENGTH('dbo.TransferCertificates','IssuedBy') IS NOT NULL ALTER TABLE [dbo].[TransferCertificates] ALTER COLUMN [IssuedBy] BIGINT NOT NULL;");
        migrationBuilder.AddColumn<Guid>(name:"PublicId", table:"TransferCertificates", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.AddColumn<Guid>(name:"ClientRequestId", table:"TransferCertificates", type:"uniqueidentifier", nullable:false, defaultValueSql:"NEWID()");
        migrationBuilder.AddColumn<int>(name:"LastSectionId", table:"TransferCertificates", type:"int", nullable:false, defaultValue:0);
        migrationBuilder.AddColumn<int>(name:"LastAcademicYearId", table:"TransferCertificates", type:"int", nullable:false, defaultValue:0);
        migrationBuilder.AddColumn<string>(name:"LastRoll", table:"TransferCertificates", type:"nvarchar(50)", maxLength:50, nullable:false, defaultValue:"");
        migrationBuilder.Sql("UPDATE t SET LastSectionId=s.SectionId, LastAcademicYearId=s.AcademicYearId, LastRoll=s.Roll FROM TransferCertificates t INNER JOIN Students s ON s.Id=t.StudentId WHERE t.LastSectionId=0;");
        migrationBuilder.CreateIndex(name:"UX_TransferCertificates_Tenant_PublicId", table:"TransferCertificates", columns:new[]{"TenantId","PublicId"}, unique:true);
        migrationBuilder.CreateIndex(name:"UX_TransferCertificates_Tenant_Request", table:"TransferCertificates", columns:new[]{"TenantId","ClientRequestId"}, unique:true, filter:"[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name:"UX_TransferCertificates_Tenant_Student", table:"TransferCertificates", columns:new[]{"TenantId","StudentId"}, unique:true, filter:"[IsDeleted] = 0");

        migrationBuilder.CreateTable(name:"StudentExitRecords", columns: table => new
        {
            Id = table.Column<long>(type:"bigint", nullable:false).Annotation("SqlServer:Identity", "1, 1"),
            PublicId = table.Column<Guid>(type:"uniqueidentifier", nullable:false), ClientRequestId = table.Column<Guid>(type:"uniqueidentifier", nullable:false),
            StudentId = table.Column<long>(type:"bigint", nullable:false), EnrollmentId = table.Column<long>(type:"bigint", nullable:true), ExitType = table.Column<string>(type:"nvarchar(20)", maxLength:20, nullable:false), CertificateNo = table.Column<string>(type:"nvarchar(50)", maxLength:50, nullable:true),
            AcademicYearId = table.Column<int>(type:"int", nullable:false), ClassId = table.Column<int>(type:"int", nullable:false), SectionId = table.Column<int>(type:"int", nullable:false), GroupId = table.Column<int>(type:"int", nullable:true), Roll = table.Column<string>(type:"nvarchar(50)", maxLength:50, nullable:false),
            DueAtExit = table.Column<decimal>(type:"decimal(18,2)", nullable:false), FeesCleared = table.Column<bool>(type:"bit", nullable:false), ProcessedAtUtc = table.Column<DateTime>(type:"datetime2", nullable:false), ProcessedByUserId = table.Column<long>(type:"bigint", nullable:false), Reason = table.Column<string>(type:"nvarchar(1000)", maxLength:1000, nullable:true), ConductRemark = table.Column<string>(type:"nvarchar(500)", maxLength:500, nullable:true),
            TenantId = table.Column<long>(type:"bigint", nullable:false), CreatedAt = table.Column<DateTime>(type:"datetime2", nullable:false), UpdatedAt = table.Column<DateTime>(type:"datetime2", nullable:true), CreatedBy = table.Column<long>(type:"bigint", nullable:true), UpdatedBy = table.Column<long>(type:"bigint", nullable:true), IsDeleted = table.Column<bool>(type:"bit", nullable:false)
        }, constraints: table => { table.PrimaryKey("PK_StudentExitRecords", x=>x.Id); table.ForeignKey("FK_StudentExitRecords_Students_StudentId", x=>x.StudentId, "Students", "Id", onDelete:ReferentialAction.Restrict); table.ForeignKey("FK_StudentExitRecords_Enrollments_EnrollmentId", x=>x.EnrollmentId, "Enrollments", "Id", onDelete:ReferentialAction.Restrict); table.ForeignKey("FK_StudentExitRecords_Tenants_TenantId", x=>x.TenantId, "Tenants", "Id", onDelete:ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex(name:"IX_StudentExitRecords_StudentId", table:"StudentExitRecords", column:"StudentId"); migrationBuilder.CreateIndex(name:"IX_StudentExitRecords_EnrollmentId", table:"StudentExitRecords", column:"EnrollmentId"); migrationBuilder.CreateIndex(name:"IX_StudentExitRecords_TenantId", table:"StudentExitRecords", column:"TenantId");
        migrationBuilder.CreateIndex(name:"UX_StudentExitRecords_Tenant_PublicId", table:"StudentExitRecords", columns:new[]{"TenantId","PublicId"}, unique:true);
        migrationBuilder.CreateIndex(name:"UX_StudentExitRecords_Tenant_Request", table:"StudentExitRecords", columns:new[]{"TenantId","ClientRequestId"}, unique:true, filter:"[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name:"UX_StudentExitRecords_Tenant_Student", table:"StudentExitRecords", columns:new[]{"TenantId","StudentId"}, unique:true, filter:"[IsDeleted] = 0");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name:"StudentExitRecords");
}
