using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive, empty-on-install immutable fee planning evidence. No historical invoice/GL rewrite.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029020000_StudentFeeInstallmentPlanEvidence")]
public sealed class StudentFeeInstallmentPlanEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("StudentFeeInstallmentPlans", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            StudentEnrollmentId = t.Column<long>("bigint", nullable: false),
            FeeStructureId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            AcademicYearId = t.Column<long>("bigint", nullable: false),
            StartMonth = t.Column<DateOnly>("date", nullable: false),
            EndMonth = t.Column<DateOnly>("date", nullable: false),
            CurrencyCode = t.Column<string>("nvarchar(10)", maxLength: 10, nullable: false),
            GrossAmount = t.Column<decimal>("decimal(18,2)", nullable: false),
            LineCount = t.Column<int>("int", nullable: false),
            PreparedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudentFeeInstallmentPlans", x => x.Id);
            t.UniqueConstraint("AK_StudentFeeInstallmentPlans_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_StudentFeeInstallmentPlans_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlans_StudentEnrollments_Tenant", x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlans_FeeStructures_Tenant", x => new { x.TenantId, x.FeeStructureId }, "FeeStructures", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlans_Campuses_Tenant", x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlans_AcademicYears_Tenant", x => new { x.TenantId, x.AcademicYearId }, "AcademicYears", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlans_Users_Preparer", x => x.PreparedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_StudentFeeInstallmentPlan_Valid", "[StartMonth] <= [EndMonth] AND [GrossAmount] > 0 AND [LineCount] BETWEEN 1 AND 600");
        });
        migrationBuilder.CreateTable("StudentFeeInstallmentPlanLines", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            StudentFeeInstallmentPlanId = t.Column<long>("bigint", nullable: false),
            FeeHeadId = t.Column<long>("bigint", nullable: false),
            MonthStart = t.Column<DateOnly>("date", nullable: false),
            DueDate = t.Column<DateOnly>("date", nullable: false),
            FeeHeadCodeSnapshot = t.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            FeeHeadNameSnapshot = t.Column<string>("nvarchar(150)", maxLength: 150, nullable: false),
            GrossAmount = t.Column<decimal>("decimal(18,2)", nullable: false),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudentFeeInstallmentPlanLines", x => x.Id);
            t.UniqueConstraint("AK_StudentFeeInstallmentPlanLines_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_StudentFeeInstallmentPlanLines_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlanLines_Plans_Tenant", x => new { x.TenantId, x.StudentFeeInstallmentPlanId }, "StudentFeeInstallmentPlans", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlanLines_Heads_Tenant", x => new { x.TenantId, x.FeeHeadId }, "FeeHeads", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_StudentFeeInstallmentPlanLine_Valid", "[GrossAmount] > 0 AND [DueDate] >= [MonthStart] AND [DueDate] < DATEADD(month, 1, [MonthStart])");
        });
        migrationBuilder.CreateTable("StudentFeeInstallmentPlanEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            StudentFeeInstallmentPlanId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ActionCode = t.Column<int>("int", nullable: false),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            EventAt = t.Column<DateTime>("datetime2", nullable: false),
            Reason = t.Column<string>("nvarchar(250)", maxLength: 250, nullable: true),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudentFeeInstallmentPlanEvents", x => x.Id);
            t.UniqueConstraint("AK_StudentFeeInstallmentPlanEvents_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_StudentFeeInstallmentPlanEvents_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlanEvents_Plans_Tenant", x => new { x.TenantId, x.StudentFeeInstallmentPlanId }, "StudentFeeInstallmentPlans", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentFeeInstallmentPlanEvents_Users_Actor", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_StudentFeeInstallmentPlanEvent_Valid", "[ActionCode] BETWEEN 2 AND 4 AND ([ActionCode] <> 4 OR ([Reason] IS NOT NULL AND LEN([Reason]) > 0))");
        });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_TenantId_AcademicYearId", "StudentFeeInstallmentPlans", new[] { "TenantId", "AcademicYearId" });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_TenantId_CampusId", "StudentFeeInstallmentPlans", new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_TenantId_FeeStructureId", "StudentFeeInstallmentPlans", new[] { "TenantId", "FeeStructureId" });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_PreparedByUserId", "StudentFeeInstallmentPlans", "PreparedByUserId");
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_PublicId", "StudentFeeInstallmentPlans", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_TenantId_ClientRequestId", "StudentFeeInstallmentPlans", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlans_TenantId_StudentEnrollmentId_StartMonth_EndMonth", "StudentFeeInstallmentPlans", new[] { "TenantId", "StudentEnrollmentId", "StartMonth", "EndMonth" });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlanLines_TenantId_FeeHeadId", "StudentFeeInstallmentPlanLines", new[] { "TenantId", "FeeHeadId" });
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlanLines_TenantId_StudentFeeInstallmentPlanId_MonthStart_FeeHeadId", "StudentFeeInstallmentPlanLines", new[] { "TenantId", "StudentFeeInstallmentPlanId", "MonthStart", "FeeHeadId" }, unique: true);
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlanEvents_ActorUserId", "StudentFeeInstallmentPlanEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlanEvents_TenantId_ClientRequestId", "StudentFeeInstallmentPlanEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_StudentFeeInstallmentPlanEvents_TenantId_StudentFeeInstallmentPlanId_ActionCode", "StudentFeeInstallmentPlanEvents", new[] { "TenantId", "StudentFeeInstallmentPlanId", "ActionCode" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [dbo].[StudentFeeInstallmentPlans])
            OR EXISTS (SELECT 1 FROM [dbo].[StudentFeeInstallmentPlanLines])
            OR EXISTS (SELECT 1 FROM [dbo].[StudentFeeInstallmentPlanEvents])
            THROW 51003, 'Installment evidence exists; rollback requires approved archival and retention.', 1;");
        migrationBuilder.DropTable("StudentFeeInstallmentPlanEvents");
        migrationBuilder.DropTable("StudentFeeInstallmentPlanLines");
        migrationBuilder.DropTable("StudentFeeInstallmentPlans");
    }
}
