using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    [Fact]
    public async Task SQL_supplier_invoice_preview_matches_posted_receipt_only_and_never_books_payable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId, poId, poLineId, locationId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "PM-" + suffix, Name = "PO invoice match SQL",
                Email = "pm-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "pm-m-" + suffix,
                NormalizedUserName = ("pm-m-" + suffix).ToUpperInvariant(),
                Email = "pm-m-" + suffix + "@example.test", FullName = "Receipt maker" };
            var checker = new ApplicationUser { UserName = "pm-c-" + suffix,
                NormalizedUserName = ("pm-c-" + suffix).ToUpperInvariant(),
                Email = "pm-c-" + suffix + "@example.test", FullName = "Receipt checker" };
            root.Tenants.Add(tenant); root.ApplicationUsers.AddRange(maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        await using (var seed = Open(options, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "PM-C-" + suffix, Name = "Campus" };
            var item = new InventoryItem { TenantId = tenantId, Code = "PM-I-" + suffix,
                Name = "Stock", IsActive = true, IsStockTracked = true };
            seed.Campuses.Add(campus); seed.InventoryItems.Add(item);
            await seed.SaveChangesAsync();
            var location = new InventoryLocation { TenantId = tenantId, CampusId = campus.Id,
                Code = "PM-L-" + suffix, Name = "Store", IsActive = true };
            var requisition = new ProcurementRequisition { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), RequestNumber = "PM-R-" + suffix,
                Purpose = "Supplier invoice evidence", StateCode = "Approved", CreatedBy = makerId };
            var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), SupplierCode = "PM-S-" + suffix,
                Name = "Supplier", StateCode = "Approved", CreatedBy = makerId };
            seed.InventoryLocations.Add(location); seed.ProcurementRequisitions.Add(requisition);
            seed.ProcurementSuppliers.Add(supplier);
            await seed.SaveChangesAsync(); locationId = location.Id;
            var rLine = new ProcurementRequisitionLine { TenantId = tenantId,
                RequisitionId = requisition.Id, LineNo = 1, InventoryItemId = item.Id,
                Quantity = 10m, EstimatedUnitPrice = 12m };
            seed.ProcurementRequisitionLines.Add(rLine); await seed.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId = tenantId,
                ClientRequestId = Guid.NewGuid(), OrderNumber = "PM-PO-" + suffix,
                CampusId = campus.Id, RequisitionId = requisition.Id, SupplierId = supplier.Id,
                StateCode = "Approved", CreatedBy = makerId,
                SubmittedAt = DateTime.UtcNow.AddMinutes(-5), DecidedAt = DateTime.UtcNow,
                DecidedByUserId = checkerId };
            seed.ProcurementPurchaseOrders.Add(po); await seed.SaveChangesAsync(); poId = po.Id;
            var poLine = new ProcurementPurchaseOrderLine { TenantId = tenantId,
                PurchaseOrderId = po.Id, RequisitionLineId = rLine.Id, LineNo = 1,
                InventoryItemId = item.Id, Quantity = 10m, UnitPrice = 12m };
            seed.ProcurementPurchaseOrderLines.Add(poLine); await seed.SaveChangesAsync(); poLineId = poLine.Id;
        }
        Guid receiptReference, movementReference;
        await using (var draftDb = Open(options, tenantId, makerId))
        {
            var inventory = Goods(draftDb, tenantId, makerId);
            var created = await inventory.CreateGoodsReceiptAsync(new CreateProcurementGoodsReceiptRequest
            {
                ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId, LocationId = locationId,
                Reason = "Supplier delivered four sealed cartons for stock",
                Lines = [new ProcurementGoodsReceiptLineInput {
                    PurchaseOrderLineId = poLineId, Quantity = 4m, LotOrBatchNo = "LOT-01" }]
            });
            Assert.True(created.Success, created.Message); receiptReference = created.Data!.Reference;
            var input = new PreviewProcurementInvoiceRequest { PurchaseOrderId = poId,
                Lines = [new PreviewProcurementInvoiceLineRequest {
                    GoodsReceiptReference = receiptReference, ReceiptLineNo = 1, Quantity = 2m }] };
            Assert.Equal(404, (await inventory.PreviewSupplierInvoiceMatchAsync(input)).StatusCode);
            var submitted = await inventory.SubmitGoodsReceiptAsync(receiptReference,
                new SubmitProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = created.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
        }
        await using (var review = Open(options, tenantId, checkerId))
        {
            var inventory = Goods(review, tenantId, checkerId);
            var before = (await inventory.GetGoodsReceiptAsync(receiptReference)).Data!;
            var posted = await inventory.ReviewGoodsReceiptAsync(receiptReference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = before.RowVersion, Decision = "Approve", ValuationMode = "ManualFinal",
                    Reason = "Independent physical receipt verification completed",
                    Valuations = [new ProcurementGoodsReceiptValuationInput { LineNo = 1, Amount = 12.5m,
                        Reason = "Approved delivery inspection and handling cost" }] });
            Assert.True(posted.Success, posted.Message);
            movementReference = posted.Data!.PostedMovementReference!.Value;
        }
        PreviewProcurementInvoiceRequest Match(decimal quantity = 2m, string mode = "Automatic",
            decimal amount = 0m, string? reason = null) => new()
        {
            PurchaseOrderId = poId, PriceMode = mode,
            Lines = [new PreviewProcurementInvoiceLineRequest { GoodsReceiptReference = receiptReference,
                ReceiptLineNo = 1, Quantity = quantity, Amount = amount, VarianceReason = reason }]
        };
        await using (var authorized = Open(options, tenantId, checkerId))
        {
            var inventory = Goods(authorized, tenantId, checkerId);
            var preview = await inventory.PreviewSupplierInvoiceMatchAsync(Match());
            Assert.True(preview.Success, preview.Message);
            Assert.Equal("PreviewOnly", preview.Data!.StatusCode);
            Assert.Equal(24m, preview.Data.ProposedAmount);
            Assert.Equal(-1m, preview.Data.DifferenceFromStockValue);
            Assert.True(preview.Data.RequiresIndependentFinanceReview);
            Assert.Equal(12.5m, Assert.Single(preview.Data.Lines).PostedStockUnitCost);
            Assert.Equal(409, (await inventory.PreviewSupplierInvoiceMatchAsync(Match(5m))).StatusCode);
            Assert.Equal(400, (await inventory.PreviewSupplierInvoiceMatchAsync(Match(2m, "ManualFinal", 14m))).StatusCode);
            var manual = await inventory.PreviewSupplierInvoiceMatchAsync(Match(2m, "ManualFinal", 14m,
                "Supplier supplied independent freight price evidence"));
            Assert.True(manual.Success, manual.Message);
            Assert.Equal(28m, manual.Data!.ProposedAmount);
            Assert.Equal(3m, manual.Data.DifferenceFromStockValue);
            var hybrid = await inventory.PreviewSupplierInvoiceMatchAsync(Match(2m, "AutoPlusManual", -0.5m,
                "Supplier discount documented in delivery invoice"));
            Assert.True(hybrid.Success, hybrid.Message);
            Assert.Equal(23m, hybrid.Data!.ProposedAmount);
            Assert.Equal(400, (await inventory.PreviewSupplierInvoiceMatchAsync(Match(2m, "Automatic", 1m))).StatusCode);
            Assert.Empty(await authorized.Journals.AsNoTracking().ToListAsync());
            Assert.Single(await authorized.InventoryMovements.AsNoTracking().ToListAsync());
        }
        await using (var other = Open(options, tenantId + 1000000, checkerId))
            Assert.Equal(404, (await Goods(other, tenantId + 1000000, checkerId)
                .PreviewSupplierInvoiceMatchAsync(Match())).StatusCode);
        // A genuine compensating reversal, not a synthetic ledger mutation, fails closed.
        await using (var changed = Open(options, tenantId, checkerId))
        {
            var inventory = Goods(changed, tenantId, checkerId);
            var original = (await inventory.GetMovementAsync(movementReference)).Data!;
            var reversed = await inventory.ReverseMovementAsync(movementReference,
                new ReverseInventoryMovementRequestDto { ClientRequestId = Guid.NewGuid(),
                    RowVersion = original.RowVersion,
                    Reason = "Authorized return of all stock to original supplier" });
            Assert.True(reversed.Success, reversed.Message);
        }
        await using (var reversed = Open(options, tenantId, checkerId))
            Assert.Equal(409, (await Goods(reversed, tenantId, checkerId)
                .PreviewSupplierInvoiceMatchAsync(Match())).StatusCode);
    }
}
