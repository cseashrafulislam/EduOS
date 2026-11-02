using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Non-posting, independently approved Facilities vendor booking projection.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261102130000_V3FacilityVendorBooking")]
public sealed class V3FacilityVendorBooking : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<long>("VendorSupplierId", "FacilityWorkOrders", type: "bigint", nullable: true);
        m.AddColumn<string>("VendorStateCode", "FacilityWorkOrders", type: "nvarchar(20)", maxLength: 20, nullable: true);
        m.AddColumn<long>("VendorProposedByUserId", "FacilityWorkOrders", type: "bigint", nullable: true);
        m.AddColumn<long>("VendorApprovedByUserId", "FacilityWorkOrders", type: "bigint", nullable: true);
        m.AddColumn<DateTime>("VendorStartUtc", "FacilityWorkOrders", type: "datetime2", nullable: true);
        m.AddColumn<DateTime>("VendorEndUtc", "FacilityWorkOrders", type: "datetime2", nullable: true);
        m.CreateIndex("IX_FacilityWorkOrders_TenantId_VendorSupplierId_VendorStateCode_VendorStartUtc_VendorEndUtc",
            "FacilityWorkOrders", new[] { "TenantId", "VendorSupplierId", "VendorStateCode", "VendorStartUtc", "VendorEndUtc" });
        m.AddForeignKey(
            name: "FK_FacilityWorkOrders_ProcurementSuppliers_TenantId_VendorSupplierId",
            table: "FacilityWorkOrders",
            columns: new[] { "TenantId", "VendorSupplierId" },
            principalTable: "ProcurementSuppliers",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        m.AddCheckConstraint("CK_FacilityWorkOrders_VendorState", "FacilityWorkOrders",
            "([VendorStateCode] IS NULL AND [VendorSupplierId] IS NULL AND [VendorStartUtc] IS NULL AND [VendorEndUtc] IS NULL AND [VendorProposedByUserId] IS NULL) OR ([VendorStateCode] IS NOT NULL AND [VendorStateCode] IN (N'Proposed',N'Approved',N'Rejected',N'Released') AND [VendorSupplierId] IS NOT NULL AND [VendorStartUtc] IS NOT NULL AND [VendorEndUtc] IS NOT NULL AND [VendorStartUtc] < [VendorEndUtc] AND [VendorProposedByUserId] IS NOT NULL AND ([VendorStateCode] <> N'Approved' OR [VendorApprovedByUserId] IS NOT NULL))");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropCheckConstraint("CK_FacilityWorkOrders_VendorState", "FacilityWorkOrders");
        m.DropForeignKey("FK_FacilityWorkOrders_ProcurementSuppliers_TenantId_VendorSupplierId", "FacilityWorkOrders");
        m.DropIndex("IX_FacilityWorkOrders_TenantId_VendorSupplierId_VendorStateCode_VendorStartUtc_VendorEndUtc", "FacilityWorkOrders");
        foreach (var name in new[] { "VendorSupplierId", "VendorStateCode", "VendorProposedByUserId",
            "VendorApprovedByUserId", "VendorStartUtc", "VendorEndUtc" })
            m.DropColumn(name, "FacilityWorkOrders");
    }
}
