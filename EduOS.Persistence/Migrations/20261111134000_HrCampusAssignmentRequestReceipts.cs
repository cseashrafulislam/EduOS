using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive, immutable campus assignment command receipts. An assignment can later
/// be closed or amended, but its original client request must remain replayable.
/// No historical assignments or employee records are changed by this migration.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261111134000_HrCampusAssignmentRequestReceipts")]
public sealed class HrCampusAssignmentRequestReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmployeeCampusAssignmentReceipts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                EmployeeCampusAssignmentId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmployeeCampusAssignmentReceipts", x => x.Id);
                table.UniqueConstraint("AK_EmployeeCampusAssignmentReceipts_TenantId_Id",
                    x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_EmployeeCampusAssignmentReceipt_DateRange",
                    "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                table.CheckConstraint("CK_EmployeeCampusAssignmentReceipt_RequestId",
                    "[ClientRequestId] <> '00000000-0000-0000-0000-000000000000'");
                table.ForeignKey("FK_EmployeeCampusAssignmentReceipts_EmployeeCampusAssignments_TenantId_EmployeeCampusAssignmentId",
                    x => new { x.TenantId, x.EmployeeCampusAssignmentId }, "EmployeeCampusAssignments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeCampusAssignmentReceipts_Employees_TenantId_EmployeeId",
                    x => new { x.TenantId, x.EmployeeId }, "Employees",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeCampusAssignmentReceipts_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EmployeeCampusAssignmentReceipts_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_EmployeeCampusAssignmentReceipts_TenantId_ClientRequestId",
            "EmployeeCampusAssignmentReceipts", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_EmployeeCampusAssignmentReceipts_TenantId_CampusId",
            "EmployeeCampusAssignmentReceipts", new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex("IX_EmployeeCampusAssignmentReceipts_TenantId_EmployeeCampusAssignmentId",
            "EmployeeCampusAssignmentReceipts", new[] { "TenantId", "EmployeeCampusAssignmentId" });
        migrationBuilder.CreateIndex("IX_EmployeeCampusAssignmentReceipts_TenantId_EmployeeId_Id",
            "EmployeeCampusAssignmentReceipts", new[] { "TenantId", "EmployeeId", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.EmployeeCampusAssignmentReceipts) THROW 51068, 'Campus assignment request receipts must be preserved before rollback.', 1;");
        migrationBuilder.DropTable("EmployeeCampusAssignmentReceipts");
    }
}
