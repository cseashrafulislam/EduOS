using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Tenant-scoped, auditable physical stock snapshots and maker/checker variance decisions.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031180000_V3InventoryPhysicalStockCounts")]
public sealed class V3InventoryPhysicalStockCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InventoryPhysicalCounts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationId = table.Column<long>(type: "bigint", nullable: false),
                Blind = table.Column<bool>(type: "bit", nullable: false),
                StateCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                StartFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SubmitFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ReviewFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                SubmitRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReviewRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SnapshotAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                SubmittedByUserId = table.Column<long>(type: "bigint", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                VarianceMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                ReviewReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryPhysicalCounts", x => x.Id);
                table.UniqueConstraint("AK_InventoryPhysicalCounts_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_InventoryPhysicalCounts_State", "[StateCode] IN (N'Draft',N'Submitted',N'Posted',N'Rejected')");
                table.CheckConstraint("CK_InventoryPhysicalCounts_Review", "([StateCode] IN (N'Draft',N'Submitted') AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL) OR ([StateCode] IN (N'Posted',N'Rejected') AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
                table.ForeignKey("FK_InventoryPhysicalCounts_InventoryLocations_TenantId_LocationId",
                    x => new { x.TenantId, x.LocationId }, "InventoryLocations", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InventoryPhysicalCounts_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "InventoryPhysicalCountLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                InventoryPhysicalCountId = table.Column<long>(type: "bigint", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                InventoryItemId = table.Column<long>(type: "bigint", nullable: false),
                LotOrBatchNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                BookQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                BookValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                ReservedQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                CountedQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                PostedVariance = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                PostedUnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                CorrectionReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                PostedMovementId = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long?>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryPhysicalCountLines", x => x.Id);
                table.UniqueConstraint("AK_InventoryPhysicalCountLines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_InventoryPhysicalCountLines_Quantities", "[LineNo] > 0 AND [BookQuantity] >= 0 AND [BookValue] >= 0 AND [ReservedQuantity] >= 0 AND ([CountedQuantity] IS NULL OR [CountedQuantity] >= 0) AND ([PostedUnitCost] IS NULL OR [PostedUnitCost] >= 0)");
                table.ForeignKey("FK_InventoryPhysicalCountLines_InventoryPhysicalCounts_TenantId_InventoryPhysicalCountId",
                    x => new { x.TenantId, x.InventoryPhysicalCountId }, "InventoryPhysicalCounts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InventoryPhysicalCountLines_InventoryItems_TenantId_InventoryItemId",
                    x => new { x.TenantId, x.InventoryItemId }, "InventoryItems", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InventoryPhysicalCountLines_InventoryMovements_TenantId_PostedMovementId",
                    x => new { x.TenantId, x.PostedMovementId }, "InventoryMovements", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InventoryPhysicalCountLines_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCounts_PublicId", "InventoryPhysicalCounts", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCounts_TenantId_ClientRequestId", "InventoryPhysicalCounts", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCounts_TenantId_LocationId_StateCode_CreatedAt", "InventoryPhysicalCounts", new[] { "TenantId", "LocationId", "StateCode", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCountLines_TenantId_InventoryPhysicalCountId_LineNo", "InventoryPhysicalCountLines", new[] { "TenantId", "InventoryPhysicalCountId", "LineNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCountLines_TenantId_InventoryPhysicalCountId_InventoryItemId_LotOrBatchNo", "InventoryPhysicalCountLines", new[] { "TenantId", "InventoryPhysicalCountId", "InventoryItemId", "LotOrBatchNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCountLines_TenantId_InventoryItemId", "InventoryPhysicalCountLines", new[] { "TenantId", "InventoryItemId" });
        migrationBuilder.CreateIndex("IX_InventoryPhysicalCountLines_TenantId_PostedMovementId", "InventoryPhysicalCountLines", new[] { "TenantId", "PostedMovementId" }, unique: true, filter: "[PostedMovementId] IS NOT NULL");
        migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_InventoryPhysicalCount_SnapshotImmutable
ON dbo.InventoryPhysicalCounts AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id = d.Id
        WHERE i.TenantId <> d.TenantId OR i.PublicId <> d.PublicId OR i.ClientRequestId <> d.ClientRequestId
            OR i.LocationId <> d.LocationId OR i.Blind <> d.Blind OR i.Reason <> d.Reason
            OR i.StartFingerprint <> d.StartFingerprint OR i.SnapshotAtUtc <> d.SnapshotAtUtc)
        THROW 51111, 'Physical count book snapshot is immutable.', 1;
END;");
        migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_InventoryPhysicalCountLine_SnapshotImmutable
ON dbo.InventoryPhysicalCountLines AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id = d.Id
        WHERE i.TenantId <> d.TenantId OR i.InventoryPhysicalCountId <> d.InventoryPhysicalCountId
            OR i.LineNo <> d.LineNo OR i.InventoryItemId <> d.InventoryItemId
            OR i.LotOrBatchNo <> d.LotOrBatchNo OR i.BookQuantity <> d.BookQuantity
            OR i.BookValue <> d.BookValue OR i.ReservedQuantity <> d.ReservedQuantity)
        THROW 51112, 'Physical count line book snapshot is immutable.', 1;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.InventoryPhysicalCounts) OR EXISTS (SELECT 1 FROM dbo.InventoryPhysicalCountLines) THROW 51110, 'Physical count audit history requires controlled archival before rollback.', 1;");
        migrationBuilder.DropTable("InventoryPhysicalCountLines");
        migrationBuilder.DropTable("InventoryPhysicalCounts");
    }
}
