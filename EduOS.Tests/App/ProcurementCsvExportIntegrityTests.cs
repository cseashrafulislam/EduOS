using System.Text;
using EduOS.App.Controllers.Api;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Synthetic service-level CSV acceptance; not SQL snapshot or authenticated browser proof.</summary>
public sealed class ProcurementCsvExportIntegrityTests
{
    private static ApiResponse<PagedResult<T>> Ok<T>(int page, int total, params T[] items) =>
        ApiResponse<PagedResult<T>>.SuccessResponse(new PagedResult<T>
        { Page = page, PageSize = 100, TotalCount = total, Items = items.ToList() });

    private static ProcurementGoodsReceiptDto Receipt(int id, decimal cost = 10m) => new()
    {
        Reference = Guid.Parse($"00000000-0000-4000-8000-{id:D12}"),
        PurchaseOrderId = 1, OrderNumber = "PO-1", CampusId = 1, LocationId = 1,
        StateCode = "Posted", PostedMovementReference = Guid.Parse("11111111-1111-4111-8111-111111111111"),
        Lines = [new ProcurementGoodsReceiptLineDto
        { LineNo = 1, PurchaseOrderLineId = 1, InventoryItemId = 1, ApprovedPoQuantity = 1m, ApprovedPoUnitPrice = 10m, LotOrBatchNo = "+SUM(1,1)\r\nUNSAFE", Quantity = 1m, PostedUnitCost = cost }]
    };

    [Fact]
    public async Task Receipt_export_rejects_duplicate_paging_and_same_count_valuation_drift()
    {
        var duplicate = new Mock<IInventoryMovementService>();
        duplicate.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100).Select(i => Receipt(i)).ToArray()));
        duplicate.Setup(x => x.GetGoodsReceiptsAsync(null, null, 2, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(2, 101, Receipt(100)));
        var a = Assert.IsType<ObjectResult>(
            await new ProcurementGoodsReceiptsController(duplicate.Object).Export(null, null, CancellationToken.None));
        Assert.Equal(409, a.StatusCode);
        var changed = new Mock<IInventoryMovementService>();
        changed.SetupSequence(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Receipt(1, 10m)))
            .ReturnsAsync(Ok(1, 1, Receipt(1, 11m)));
        var b = Assert.IsType<ObjectResult>(
            await new ProcurementGoodsReceiptsController(changed.Object).Export(null, null, CancellationToken.None));
        Assert.Equal(409, b.StatusCode);
    }

    [Fact]
    public async Task Receipt_export_rechecks_access_and_sanitizes_stable_csv()
    {
        var denied = new Mock<IInventoryMovementService>();
        denied.SetupSequence(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Receipt(1)))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementGoodsReceiptDto>>.ErrorResponse("Revoked", 403));
        var r = Assert.IsType<ObjectResult>(
            await new ProcurementGoodsReceiptsController(denied.Object).Export(null, null, CancellationToken.None));
        Assert.Equal(403, r.StatusCode);
        var stable = new Mock<IInventoryMovementService>();
        stable.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, Receipt(1)));
        var file = Assert.IsType<FileContentResult>(
            await new ProcurementGoodsReceiptsController(stable.Object).Export(null, null, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("'+SUM(1,1)  UNSAFE", csv);
        Assert.DoesNotContain("\r\nUNSAFE", csv);
        Assert.Contains("\"10\"", csv);
    }


    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("zero-quantity")]
    [InlineData("missing-po-line")]
    [InlineData("missing-item")]
    [InlineData("over-limit")]
    [InlineData("posted-without-movement")]
    [InlineData("posted-without-cost")]
    [InlineData("negative-posted-cost")]
    public async Task Receipt_export_refuses_incomplete_or_unlinked_stock_source_evidence(string defect)
    {
        var receipt = Receipt(1);
        switch (defect)
        {
            case "missing":
                receipt.Lines.Clear();
                break;
            case "duplicate":
                receipt.Lines.Add(new ProcurementGoodsReceiptLineDto
                {
                    LineNo = 1, PurchaseOrderLineId = 2, InventoryItemId = 1,
                    Quantity = 1m, ApprovedPoQuantity = 1m, ApprovedPoUnitPrice = 10m
                });
                break;
            case "zero-quantity":
                receipt.Lines[0].Quantity = 0m;
                break;
            case "missing-po-line":
                receipt.Lines[0].PurchaseOrderLineId = 0;
                break;
            case "missing-item":
                receipt.Lines[0].InventoryItemId = 0;
                break;
            case "over-limit":
                receipt.Lines = Enumerable.Range(1, 101).Select(i =>
                    new ProcurementGoodsReceiptLineDto
                    {
                        LineNo = i, PurchaseOrderLineId = i, InventoryItemId = 1,
                        Quantity = 1m, ApprovedPoQuantity = 1m, ApprovedPoUnitPrice = 10m
                    }).ToList();
                break;
            case "posted-without-movement":
                receipt.PostedMovementReference = null;
                break;
            case "posted-without-cost":
                receipt.StateCode = "Posted";
                receipt.PostedMovementReference = Guid.NewGuid();
                receipt.Lines[0].PostedUnitCost = null;
                break;
            case "negative-posted-cost":
                receipt.StateCode = "Posted";
                receipt.PostedMovementReference = Guid.NewGuid();
                receipt.Lines[0].PostedUnitCost = -1m;
                break;
        }
        var inventory = new Mock<IInventoryMovementService>();
        inventory.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, receipt));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementGoodsReceiptsController(inventory.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
        Assert.IsNotType<FileContentResult>(result);
    }

    [Fact]
    public async Task Posted_receipt_export_preserves_valid_stock_movement_lineage()
    {
        var receipt = Receipt(1);
        receipt.StateCode = "Posted";
        receipt.PostedMovementReference = Guid.NewGuid();
        var inventory = new Mock<IInventoryMovementService>();
        inventory.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, receipt));
        var result = Assert.IsType<FileContentResult>(
            await new ProcurementGoodsReceiptsController(inventory.Object)
                .Export(null, null, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(result.FileContents);
        Assert.Contains(receipt.PostedMovementReference.Value.ToString(), csv);
        Assert.Contains("\"10\"", csv);
    }


    [Theory]
    [InlineData("missing-po")]
    [InlineData("missing-order-number")]
    [InlineData("missing-campus")]
    [InlineData("missing-location")]
    [InlineData("invalid-state")]
    [InlineData("overreceipt")]
    [InlineData("quantity-precision")]
    [InlineData("approved-quantity-precision")]
    [InlineData("po-price-precision")]
    [InlineData("posted-cost-precision")]
    [InlineData("zero-posted-cost")]
    [InlineData("missing-override-reason")]
    [InlineData("split-inconsistent-po-quantity")]
    [InlineData("split-inconsistent-po-price")]
    [InlineData("split-inconsistent-item")]
    [InlineData("split-aggregate-overreceipt")]
    [InlineData("unposted-with-movement")]
    [InlineData("unposted-with-cost")]
    public async Task Receipt_csv_rejects_header_precision_split_lot_and_posting_evidence_conflicts(string defect)
    {
        var receipt = Receipt(1);
        switch (defect)
        {
            case "missing-po": receipt.PurchaseOrderId = 0; break;
            case "missing-order-number": receipt.OrderNumber = ""; break;
            case "missing-campus": receipt.CampusId = 0; break;
            case "missing-location": receipt.LocationId = 0; break;
            case "invalid-state": receipt.StateCode = "Unknown"; break;
            case "overreceipt": receipt.Lines[0].Quantity = 2m; break;
            case "quantity-precision": receipt.Lines[0].Quantity = 0.1234567m; break;
            case "approved-quantity-precision": receipt.Lines[0].ApprovedPoQuantity = 1.0000001m; break;
            case "po-price-precision": receipt.Lines[0].ApprovedPoUnitPrice = 10.12345m; break;
            case "posted-cost-precision": receipt.Lines[0].PostedUnitCost = 10.12345m; break;
            case "zero-posted-cost": receipt.Lines[0].PostedUnitCost = 0m; break;
            case "missing-override-reason": receipt.Lines[0].PostedUnitCost = 11m; break;
            case "split-inconsistent-po-quantity":
            case "split-inconsistent-po-price":
            case "split-inconsistent-item":
            case "split-aggregate-overreceipt":
                receipt.Lines[0].Quantity = 0.6m;
                receipt.Lines.Add(new ProcurementGoodsReceiptLineDto
                {
                    LineNo = 2, PurchaseOrderLineId = 1, InventoryItemId = 1,
                    Quantity = defect == "split-aggregate-overreceipt" ? 0.5m : 0.4m,
                    ApprovedPoQuantity = defect == "split-inconsistent-po-quantity" ? 2m : 1m,
                    ApprovedPoUnitPrice = defect == "split-inconsistent-po-price" ? 12m : 10m,
                    PostedUnitCost = 10m, LotOrBatchNo = "SECOND-LOT"
                });
                if (defect == "split-inconsistent-item") receipt.Lines[1].InventoryItemId = 2;
                break;
            case "unposted-with-movement":
                receipt.StateCode = "Draft"; receipt.Lines[0].PostedUnitCost = null; break;
            case "unposted-with-cost":
                receipt.StateCode = "Submitted"; receipt.PostedMovementReference = null; break;
        }
        var inventory = new Mock<IInventoryMovementService>();
        inventory.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, receipt));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementGoodsReceiptsController(inventory.Object)
                .Export(null, null, CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Receipt_csv_accepts_two_lots_of_one_po_line_with_four_decimal_valuation()
    {
        var receipt = Receipt(1, 10.1234m);
        receipt.Lines[0].Quantity = 0.4m;
        receipt.Lines[0].ApprovedPoUnitPrice = 10.1234m;
        receipt.Lines.Add(new ProcurementGoodsReceiptLineDto
        {
            LineNo = 2, PurchaseOrderLineId = 1, InventoryItemId = 1,
            Quantity = 0.6m, ApprovedPoQuantity = 1m, ApprovedPoUnitPrice = 10.1234m,
            PostedUnitCost = 10.1234m, LotOrBatchNo = "LOT-B"
        });
        var inventory = new Mock<IInventoryMovementService>();
        inventory.Setup(x => x.GetGoodsReceiptsAsync(null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, receipt));
        var file = Assert.IsType<FileContentResult>(
            await new ProcurementGoodsReceiptsController(inventory.Object)
                .Export(null, null, CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("LOT-B", csv);
        Assert.Contains("\"10.1234\"", csv);
        Assert.Contains(receipt.PostedMovementReference!.Value.ToString(), csv);
    }

    [Fact]
    public async Task Commitment_export_rejects_same_count_approved_amount_drift()
    {
        var po = new Mock<IProcurementPurchaseOrderService>();
        po.SetupSequence(x => x.CommitmentsAsync(1, 100, null, null, null, false, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, new ProcurementPurchaseOrderCommitmentDto { Id = 1, ApprovedAmount = 10m }))
            .ReturnsAsync(Ok(1, 1, new ProcurementPurchaseOrderCommitmentDto { Id = 1, ApprovedAmount = 11m }));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementPurchaseOrdersController(po.Object).ExportCommitments(ct: CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Purchase_order_export_rejects_revocation()
    {
        var po = new Mock<IProcurementPurchaseOrderService>();
        po.SetupSequence(x => x.ListAsync(1, 100, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, new ProcurementPurchaseOrderDto { Id = 1 }))
            .ReturnsAsync(ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>.ErrorResponse("Revoked", 403));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementPurchaseOrdersController(po.Object).Export(ct: CancellationToken.None));
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Requisition_export_rejects_duplicate_second_page()
    {
        var req = new Mock<IProcurementRequisitionService>();
        req.Setup(x => x.ListAsync(1, 100, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 101, Enumerable.Range(1, 100)
                .Select(i => new ProcurementRequisitionDto { Id = i }).ToArray()));
        req.Setup(x => x.ListAsync(2, 100, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(2, 101, new ProcurementRequisitionDto { Id = 100 }));
        var result = Assert.IsType<ObjectResult>(
            await new ProcurementRequisitionsController(req.Object).Export(ct: CancellationToken.None));
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Supplier_confirmation_export_neutralizes_formula_and_embedded_newline()
    {
        var supplier = new Mock<IProcurementSupplierAcknowledgementService>();
        supplier.Setup(x => x.ListAsync(1, 100, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, new ProcurementSupplierAcknowledgementDto
            { Id = 1, SupplierId = 3, SupplierReference = "=HYPERLINK(\"unsafe\")\r\nEXTRA" }));
        var file = Assert.IsType<FileContentResult>(
            await new ProcurementSupplierAcknowledgementsController(supplier.Object).Export(ct: CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain("\r\nEXTRA", csv);
        Assert.Contains("\"3\"", csv);
    }
    [Fact]
    public async Task Supplier_confirmation_export_neutralizes_invisible_formula_prefix_without_corrupting_Bangla()
    {
        var supplier = new Mock<IProcurementSupplierAcknowledgementService>();
        supplier.Setup(x => x.ListAsync(1, 100, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(1, 1, new ProcurementSupplierAcknowledgementDto
            {
                Id = 1, SupplierId = 3,
                SupplierReference = "\u200B=HYPERLINK(\"unsafe\")\u2028NEXT বাংলা\u200Dনাম"
            }));
        var file = Assert.IsType<FileContentResult>(
            await new ProcurementSupplierAcknowledgementsController(supplier.Object).Export(ct: CancellationToken.None));
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("\"' =HYPERLINK(\"\"unsafe\"\") NEXT বাংলা\u200Dনাম\"", csv);
        Assert.False(csv.Contains("\u200B", StringComparison.Ordinal));
        Assert.False(csv.Contains("\u2028", StringComparison.Ordinal));
        Assert.Contains("\"3\"", csv);
    }

}
