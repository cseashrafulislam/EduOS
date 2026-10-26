using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive occupied-stay transfer lineage for the existing canonical allocation.
/// No historical rows are rewritten: NULL means no recorded predecessor.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261027003000_HostelOccupiedTransferLineage")]
public sealed class HostelOccupiedTransferLineage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "PreviousAllocationId",
            table: "StudentHostelAllocations",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_StudentHostelAllocations_TenantId_PreviousAllocationId",
            table: "StudentHostelAllocations",
            columns: new[] { "TenantId", "PreviousAllocationId" },
            unique: true,
            filter: "[PreviousAllocationId] IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_StudentHostelAllocations_StudentHostelAllocations_TenantId_PreviousAllocationId",
            table: "StudentHostelAllocations",
            columns: new[] { "TenantId", "PreviousAllocationId" },
            principalTable: "StudentHostelAllocations",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddCheckConstraint(
            name: "CK_StudentHostelAllocation_Predecessor",
            table: "StudentHostelAllocations",
            sql: "[PreviousAllocationId] IS NULL OR [PreviousAllocationId] <> [Id]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_StudentHostelAllocation_Predecessor",
            table: "StudentHostelAllocations");

        migrationBuilder.DropForeignKey(
            name: "FK_StudentHostelAllocations_StudentHostelAllocations_TenantId_PreviousAllocationId",
            table: "StudentHostelAllocations");

        migrationBuilder.DropIndex(
            name: "IX_StudentHostelAllocations_TenantId_PreviousAllocationId",
            table: "StudentHostelAllocations");

        migrationBuilder.DropColumn(
            name: "PreviousAllocationId",
            table: "StudentHostelAllocations");
    }
}
