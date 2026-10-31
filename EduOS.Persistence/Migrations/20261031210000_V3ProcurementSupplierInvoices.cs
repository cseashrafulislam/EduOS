using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Canonical, tenant-owned supplier invoice receipt matching and payable provenance.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031210000_V3ProcurementSupplierInvoices")]
public sealed class V3ProcurementSupplierInvoices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ProcurementSupplierInvoices", columns: table => new
        {
            Id = table.Column<long>(type:"bigint",nullable:false).Annotation("SqlServer:Identity","1, 1"),
            PublicId = table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            ClientRequestId = table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            PurchaseOrderId = table.Column<long>(type:"bigint",nullable:false),
            SupplierId = table.Column<long>(type:"bigint",nullable:false),
            CampusId = table.Column<long>(type:"bigint",nullable:false),
            SupplierInvoiceNumber = table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
            NormalizedSupplierInvoiceNumber = table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
            InvoiceDate = table.Column<DateOnly>(type:"date",nullable:false),
            DueDate = table.Column<DateOnly>(type:"date",nullable:false),
            StateCode = table.Column<string>(type:"nvarchar(25)",maxLength:25,nullable:false),
            PriceMode = table.Column<string>(type:"nvarchar(25)",maxLength:25,nullable:false),
            Reason = table.Column<string>(type:"nvarchar(800)",maxLength:800,nullable:false),
            CreateFingerprint = table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:false),
            SubmitRequestId = table.Column<Guid?>(type:"uniqueidentifier",nullable:true),
            SubmittedAtUtc = table.Column<DateTime?>(type:"datetime2",nullable:true),
            SubmittedByUserId = table.Column<long?>(type:"bigint",nullable:true),
            ReviewRequestId = table.Column<Guid?>(type:"uniqueidentifier",nullable:true),
            ReviewFingerprint = table.Column<string>(type:"nvarchar(64)",maxLength:64,nullable:true),
            ReviewedAtUtc = table.Column<DateTime?>(type:"datetime2",nullable:true),
            ReviewedByUserId = table.Column<long?>(type:"bigint",nullable:true),
            ReviewReason = table.Column<string>(type:"nvarchar(800)",maxLength:800,nullable:true),
            TotalAmount = table.Column<decimal>(type:"decimal(18,2)",nullable:false),
            StockValue = table.Column<decimal>(type:"decimal(18,2)",nullable:false),
            PriceVariance = table.Column<decimal>(type:"decimal(18,2)",nullable:false),
            JournalId = table.Column<long?>(type:"bigint",nullable:true),
            ReversalJournalId = table.Column<long?>(type:"bigint",nullable:true),
            CreatedAt = table.Column<DateTime>(type:"datetime2",nullable:false),
            UpdatedAt = table.Column<DateTime?>(type:"datetime2",nullable:true),
            CreatedBy = table.Column<long?>(type:"bigint",nullable:true),
            UpdatedBy = table.Column<long?>(type:"bigint",nullable:true),
            IsDeleted = table.Column<bool>(type:"bit",nullable:false),
            DeletedAt = table.Column<DateTime?>(type:"datetime2",nullable:true),
            DeletedBy = table.Column<long?>(type:"bigint",nullable:true),
            RowVersion = table.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false),
            TenantId = table.Column<long>(type:"bigint",nullable:false)
        },constraints:table=>
        {
            table.PrimaryKey("PK_ProcurementSupplierInvoices",x=>x.Id);
            table.UniqueConstraint("AK_ProcurementSupplierInvoices_TenantId_Id",x=>new{x.TenantId,x.Id});
            table.CheckConstraint("CK_ProcurementSupplierInvoice_State","[StateCode] IN (N'Draft',N'Submitted',N'Posted',N'Rejected',N'Reversed')");
            table.CheckConstraint("CK_ProcurementSupplierInvoice_Totals","[TotalAmount] >= 0 AND [StockValue] >= 0 AND [PriceVariance] = [TotalAmount] - [StockValue] AND [DueDate] >= [InvoiceDate]");
            table.ForeignKey("FK_ProcurementSupplierInvoices_Tenants_TenantId",x=>x.TenantId,"Tenants","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoices_ProcurementPurchaseOrders_TenantId_PurchaseOrderId",x=>new{x.TenantId,x.PurchaseOrderId},"ProcurementPurchaseOrders",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoices_ProcurementSuppliers_TenantId_SupplierId",x=>new{x.TenantId,x.SupplierId},"ProcurementSuppliers",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoices_Campuses_TenantId_CampusId",x=>new{x.TenantId,x.CampusId},"Campuses",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoices_Journals_TenantId_JournalId",x=>new{x.TenantId,x.JournalId},"Journals",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoices_Journals_TenantId_ReversalJournalId",x=>new{x.TenantId,x.ReversalJournalId},"Journals",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("ProcurementSupplierInvoiceLines",columns:table=>new
        {
            Id = table.Column<long>(type:"bigint",nullable:false).Annotation("SqlServer:Identity","1, 1"),
            SupplierInvoiceId = table.Column<long>(type:"bigint",nullable:false),
            LineNo = table.Column<int>(type:"int",nullable:false),
            GoodsReceiptLineId = table.Column<long>(type:"bigint",nullable:false),
            PurchaseOrderLineId = table.Column<long>(type:"bigint",nullable:false),
            InventoryItemId = table.Column<long>(type:"bigint",nullable:false),
            LotOrBatchNo = table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:false),
            Quantity = table.Column<decimal>(type:"decimal(18,6)",nullable:false),
            ReceivedQuantitySnapshot = table.Column<decimal>(type:"decimal(18,6)",nullable:false),
            PoUnitPriceSnapshot = table.Column<decimal>(type:"decimal(18,4)",nullable:false),
            StockUnitCostSnapshot = table.Column<decimal>(type:"decimal(18,4)",nullable:false),
            InvoiceUnitPrice = table.Column<decimal>(type:"decimal(18,4)",nullable:false),
            InvoiceAmount = table.Column<decimal>(type:"decimal(18,2)",nullable:false),
            StockValue = table.Column<decimal>(type:"decimal(18,2)",nullable:false),
            VarianceReason = table.Column<string>(type:"nvarchar(800)",maxLength:800,nullable:true),
            CreatedAt = table.Column<DateTime>(type:"datetime2",nullable:false),
            UpdatedAt = table.Column<DateTime?>(type:"datetime2",nullable:true),
            CreatedBy = table.Column<long?>(type:"bigint",nullable:true),
            UpdatedBy = table.Column<long?>(type:"bigint",nullable:true),
            IsDeleted = table.Column<bool>(type:"bit",nullable:false),
            DeletedAt = table.Column<DateTime?>(type:"datetime2",nullable:true),
            DeletedBy = table.Column<long?>(type:"bigint",nullable:true),
            RowVersion = table.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false),
            TenantId = table.Column<long>(type:"bigint",nullable:false)
        },constraints:table=>
        {
            table.PrimaryKey("PK_ProcurementSupplierInvoiceLines",x=>x.Id);
            table.UniqueConstraint("AK_ProcurementSupplierInvoiceLines_TenantId_Id",x=>new{x.TenantId,x.Id});
            table.CheckConstraint("CK_ProcurementSupplierInvoiceLine_Values","[LineNo] > 0 AND [Quantity] > 0 AND [Quantity] <= [ReceivedQuantitySnapshot] AND [PoUnitPriceSnapshot] >= 0 AND [StockUnitCostSnapshot] > 0 AND [InvoiceUnitPrice] > 0 AND [InvoiceAmount] >= 0 AND [StockValue] >= 0");
            table.ForeignKey("FK_ProcurementSupplierInvoiceLines_Tenants_TenantId",x=>x.TenantId,"Tenants","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoiceLines_ProcurementSupplierInvoices_TenantId_SupplierInvoiceId",x=>new{x.TenantId,x.SupplierInvoiceId},"ProcurementSupplierInvoices",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoiceLines_ProcurementGoodsReceiptLines_TenantId_GoodsReceiptLineId",x=>new{x.TenantId,x.GoodsReceiptLineId},"ProcurementGoodsReceiptLines",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoiceLines_ProcurementPurchaseOrderLines_TenantId_PurchaseOrderLineId",x=>new{x.TenantId,x.PurchaseOrderLineId},"ProcurementPurchaseOrderLines",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSupplierInvoiceLines_InventoryItems_TenantId_InventoryItemId",x=>new{x.TenantId,x.InventoryItemId},"InventoryItems",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_PublicId","ProcurementSupplierInvoices","PublicId",unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_ClientRequestId","ProcurementSupplierInvoices",new[]{"TenantId","ClientRequestId"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_SupplierId_NormalizedSupplierInvoiceNumber","ProcurementSupplierInvoices",new[]{"TenantId","SupplierId","NormalizedSupplierInvoiceNumber"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_PurchaseOrderId_StateCode","ProcurementSupplierInvoices",new[]{"TenantId","PurchaseOrderId","StateCode"});
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_CampusId_StateCode_InvoiceDate","ProcurementSupplierInvoices",new[]{"TenantId","CampusId","StateCode","InvoiceDate"});
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_JournalId","ProcurementSupplierInvoices",new[]{"TenantId","JournalId"},unique:true,filter:"[JournalId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoices_TenantId_ReversalJournalId","ProcurementSupplierInvoices",new[]{"TenantId","ReversalJournalId"},unique:true,filter:"[ReversalJournalId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoiceLines_TenantId_SupplierInvoiceId_LineNo","ProcurementSupplierInvoiceLines",new[]{"TenantId","SupplierInvoiceId","LineNo"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoiceLines_TenantId_SupplierInvoiceId_GoodsReceiptLineId","ProcurementSupplierInvoiceLines",new[]{"TenantId","SupplierInvoiceId","GoodsReceiptLineId"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoiceLines_TenantId_GoodsReceiptLineId","ProcurementSupplierInvoiceLines",new[]{"TenantId","GoodsReceiptLineId"});
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoiceLines_TenantId_PurchaseOrderLineId","ProcurementSupplierInvoiceLines",new[]{"TenantId","PurchaseOrderLineId"});
        migrationBuilder.CreateIndex("IX_ProcurementSupplierInvoiceLines_TenantId_InventoryItemId","ProcurementSupplierInvoiceLines",new[]{"TenantId","InventoryItemId"});
        migrationBuilder.CreateIndex("UX_Journals_ProcurementSupplierInvoice","Journals",new[]{"TenantId","SourceId"},unique:true,
            filter:"[SourceType] = N'ProcurementSupplierInvoice' AND [SourceId] IS NOT NULL");
        migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER dbo.TR_ProcurementSupplierInvoice_SourceImmutable ON dbo.ProcurementSupplierInvoices AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE
     i.TenantId<>d.TenantId OR i.PublicId<>d.PublicId OR i.ClientRequestId<>d.ClientRequestId OR
     i.PurchaseOrderId<>d.PurchaseOrderId OR i.SupplierId<>d.SupplierId OR i.CampusId<>d.CampusId OR
     i.SupplierInvoiceNumber<>d.SupplierInvoiceNumber OR i.NormalizedSupplierInvoiceNumber<>d.NormalizedSupplierInvoiceNumber OR
     i.InvoiceDate<>d.InvoiceDate OR i.DueDate<>d.DueDate OR i.PriceMode<>d.PriceMode OR
     i.Reason<>d.Reason OR i.CreateFingerprint<>d.CreateFingerprint OR
     i.TotalAmount<>d.TotalAmount OR i.StockValue<>d.StockValue OR i.PriceVariance<>d.PriceVariance OR
     (d.StateCode IN (N'Submitted',N'Posted',N'Reversed') AND i.IsDeleted=1))
     THROW 51130, 'Immutable supplier invoice source cannot change.',1;
END;");
        migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER dbo.TR_ProcurementSupplierInvoiceLine_SourceImmutable ON dbo.ProcurementSupplierInvoiceLines AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE
     i.TenantId<>d.TenantId OR i.SupplierInvoiceId<>d.SupplierInvoiceId OR i.[LineNo]<>d.[LineNo] OR
     i.GoodsReceiptLineId<>d.GoodsReceiptLineId OR i.PurchaseOrderLineId<>d.PurchaseOrderLineId OR
     i.InventoryItemId<>d.InventoryItemId OR i.LotOrBatchNo<>d.LotOrBatchNo OR
     i.Quantity<>d.Quantity OR i.ReceivedQuantitySnapshot<>d.ReceivedQuantitySnapshot OR
     i.PoUnitPriceSnapshot<>d.PoUnitPriceSnapshot OR i.StockUnitCostSnapshot<>d.StockUnitCostSnapshot OR
     i.InvoiceUnitPrice<>d.InvoiceUnitPrice OR i.InvoiceAmount<>d.InvoiceAmount OR
     i.StockValue<>d.StockValue OR i.IsDeleted<>d.IsDeleted)
     THROW 51131, 'Immutable invoice receipt-line evidence cannot change.',1;
END;");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.ProcurementSupplierInvoices) OR EXISTS(SELECT 1 FROM dbo.ProcurementSupplierInvoiceLines) THROW 51132, 'Supplier invoice/payment history requires controlled retention before rollback.',1;");
        migrationBuilder.DropIndex("UX_Journals_ProcurementSupplierInvoice","Journals");
        migrationBuilder.DropTable("ProcurementSupplierInvoiceLines");
        migrationBuilder.DropTable("ProcurementSupplierInvoices");
    }
}
