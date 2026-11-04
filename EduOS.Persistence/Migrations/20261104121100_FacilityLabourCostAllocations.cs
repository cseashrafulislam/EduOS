using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive, management-only PayrollRun → technician → verified work-order
/// allocation history. Never changes existing posted payroll or GL journals.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104121100_FacilityLabourCostAllocations")]
public sealed class FacilityLabourCostAllocations : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        // Existing Id primary keys already guarantee uniqueness. These additive
        // composite alternate keys also let SQL Server enforce same-tenant FKs.
        // Preflight and schedule index/constraint locks on production-size tables.
        m.AddUniqueConstraint("AK_PayrollEmployees_TenantId_Id",
            "PayrollEmployees", new[] { "TenantId", "Id" });
        m.AddUniqueConstraint("AK_PayrollRuns_TenantId_Id",
            "PayrollRuns", new[] { "TenantId", "Id" });
        m.AddUniqueConstraint("AK_Employees_TenantId_Id",
            "Employees", new[] { "TenantId", "Id" });
        m.CreateTable("FacilityLabourCostAllocations", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            FacilityWorkOrderId = t.Column<long>(type: "bigint", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            PayrollEmployeeId = t.Column<long>(type: "bigint", nullable: false),
            EmployeeId = t.Column<long>(type: "bigint", nullable: false),
            PayrollRunId = t.Column<long>(type: "bigint", nullable: false),
            ActualWorkMinutes = t.Column<int>(type: "int", nullable: false),
            StandardPayrollMinutesSnapshot = t.Column<int>(type: "int", nullable: false),
            PayrollGrossSnapshotBdt = t.Column<decimal>(type: "decimal(18,2)", nullable: false),
            AllocatedAmountBdt = t.Column<decimal>(type: "decimal(18,2)", nullable: false),
            StateCode = t.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
            Evidence = t.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
            RequestedByUserId = t.Column<long>(type: "bigint", nullable: false),
            ReviewRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReviewFingerprint = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
            ReviewedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            ReviewReason = t.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
            CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = t.Column<long>(type: "bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = t.Column<long>(type: "bigint", nullable: true),
            IsDeleted = t.Column<bool>(type: "bit", nullable: false),
            DeletedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = t.Column<long>(type: "bigint", nullable: true),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_FacilityLabourCostAllocations", x => x.Id);
            t.UniqueConstraint("AK_FacilityLabourCostAllocations_TenantId_Id",
                x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityLabourCostAllocations_Tenants_TenantId",
                x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityLabourCostAllocations_FacilityWorkOrders_TenantId_FacilityWorkOrderId",
                x => new { x.TenantId, x.FacilityWorkOrderId },
                "FacilityWorkOrders", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityLabourCostAllocations_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityLabourCostAllocations_PayrollEmployees_TenantId_PayrollEmployeeId",
                x => new { x.TenantId, x.PayrollEmployeeId },
                "PayrollEmployees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityLabourCostAllocations_PayrollRuns_TenantId_PayrollRunId",
                x => new { x.TenantId, x.PayrollRunId },
                "PayrollRuns", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityLabourCostAllocations_Employees_TenantId_EmployeeId",
                x => new { x.TenantId, x.EmployeeId },
                "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_PublicId",
            "FacilityLabourCostAllocations", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_ClientRequestId",
            "FacilityLabourCostAllocations", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_ReviewRequestId",
            "FacilityLabourCostAllocations", new[] { "TenantId", "ReviewRequestId" }, unique: true,
            filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_FacilityWorkOrderId_PayrollEmployeeId",
            "FacilityLabourCostAllocations",
            new[] { "TenantId", "FacilityWorkOrderId", "PayrollEmployeeId" }, unique: true,
            filter: "[IsDeleted] = 0 AND [StateCode] <> N'Rejected'");
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_PayrollEmployeeId_StateCode",
            "FacilityLabourCostAllocations", new[] { "TenantId", "PayrollEmployeeId", "StateCode" });
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_CampusId_StateCode",
            "FacilityLabourCostAllocations", new[] { "TenantId", "CampusId", "StateCode" });
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_EmployeeId",
            "FacilityLabourCostAllocations", new[] { "TenantId", "EmployeeId" });
        m.CreateIndex("IX_FacilityLabourCostAllocations_TenantId_PayrollRunId",
            "FacilityLabourCostAllocations", new[] { "TenantId", "PayrollRunId" });
        m.AddCheckConstraint("CK_FacilityLabourCostAllocations_StateAmount",
            "FacilityLabourCostAllocations",
            "[StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND " +
            "[ActualWorkMinutes] > 0 AND [StandardPayrollMinutesSnapshot] >= [ActualWorkMinutes] AND " +
            "[PayrollGrossSnapshotBdt] > 0 AND [AllocatedAmountBdt] > 0 AND " +
            "[AllocatedAmountBdt] <= [PayrollGrossSnapshotBdt]");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("FacilityLabourCostAllocations");
        m.DropUniqueConstraint("AK_Employees_TenantId_Id", "Employees");
        m.DropUniqueConstraint("AK_PayrollRuns_TenantId_Id", "PayrollRuns");
        m.DropUniqueConstraint("AK_PayrollEmployees_TenantId_Id", "PayrollEmployees");
    }
}
