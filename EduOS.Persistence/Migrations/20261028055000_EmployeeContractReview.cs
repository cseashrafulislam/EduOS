using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261028055000_EmployeeContractReview")]
public sealed class EmployeeContractReview : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("EmployeeContracts", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            EmployeeId = t.Column<long>(type: "bigint", nullable: false),
            ContractCode = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
            EffectiveFrom = t.Column<DateOnly>(type: "date", nullable: false),
            EffectiveTo = t.Column<DateOnly>(type: "date", nullable: true),
            Remarks = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            State = t.Column<int>(type: "int", nullable: false),
            ReviewedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            ReviewedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ReviewReason = t.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = t.Column<long>(type: "bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = t.Column<long>(type: "bigint", nullable: true),
            IsDeleted = t.Column<bool>(type: "bit", nullable: false),
            DeletedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = t.Column<long>(type: "bigint", nullable: true),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t => {
            t.PrimaryKey("PK_EmployeeContracts", x => x.Id);
            t.UniqueConstraint("AK_EmployeeContracts_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_EmployeeContract_Dates", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            t.ForeignKey("FK_EmployeeContracts_Employees_TenantId_EmployeeId",
                x => new { x.TenantId, x.EmployeeId }, "Employees", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_EmployeeContracts_PublicId", "EmployeeContracts", "PublicId", unique: true);
        m.CreateIndex("IX_EmployeeContracts_TenantId_ClientRequestId", "EmployeeContracts",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_EmployeeContracts_TenantId_ContractCode", "EmployeeContracts",
            new[] { "TenantId", "ContractCode" }, unique: true);
        m.CreateIndex("IX_EmployeeContracts_TenantId_EmployeeId_EffectiveFrom", "EmployeeContracts",
            new[] { "TenantId", "EmployeeId", "EffectiveFrom" });
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("EmployeeContracts");
}
