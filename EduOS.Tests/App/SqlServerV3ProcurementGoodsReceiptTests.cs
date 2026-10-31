using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3ProcurementPurchaseOrderTests
{
    private static InventoryMovementService Goods(EduOSDbContext context, long tenant, long userId)
        => new(context, new Actor(tenant, userId), TimeProvider.System,
            NullLogger<InventoryMovementService>.Instance);

    [Fact]
    public async Task SQL_goods_receipt_partial_maker_checker_cost_modes_replay_and_stock_ledger()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId, campusId, storeId, poId, poLineId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "GR-" + suffix, Name = "Goods receipt SQL",
                Email = "gr-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "gr-m-" + suffix,
                NormalizedUserName = ("gr-m-" + suffix).ToUpperInvariant(),
                Email = "gr-m-" + suffix + "@example.test", FullName = "Delivery maker" };
            var checker = new ApplicationUser { UserName = "gr-c-" + suffix,
                NormalizedUserName = ("gr-c-" + suffix).ToUpperInvariant(),
                Email = "gr-c-" + suffix + "@example.test", FullName = "Stock checker" };
            root.Tenants.Add(tenant); root.ApplicationUsers.AddRange(maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        await using (var seed = Open(options, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "GR-C-" + suffix, Name = "Campus" };
            var item = new InventoryItem { TenantId = tenantId, Code = "GR-I-" + suffix,
                Name = "Stock item", IsActive = true, IsStockTracked = true };
            seed.Campuses.Add(campus); seed.InventoryItems.Add(item);
            await seed.SaveChangesAsync(); campusId = campus.Id;
            var store = new InventoryLocation { TenantId = tenantId, CampusId = campus.Id,
                Code = "GR-L-" + suffix, Name = "Receiving store", IsActive = true };
            var requisition = new ProcurementRequisition { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), RequestNumber = "GR-R-" + suffix,
                Purpose = "Stock for semester", StateCode = "Approved", CreatedBy = makerId };
            var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), SupplierCode = "GR-S-" + suffix,
                Name = "Supplier", StateCode = "Approved", CreatedBy = makerId };
            seed.InventoryLocations.Add(store);
            seed.ProcurementRequisitions.Add(requisition); seed.ProcurementSuppliers.Add(supplier);
            await seed.SaveChangesAsync(); storeId = store.Id;
            var reqLine = new ProcurementRequisitionLine { TenantId = tenantId,
                RequisitionId = requisition.Id, LineNo = 1, InventoryItemId = item.Id,
                Quantity = 10m, EstimatedUnitPrice = 5m };
            seed.ProcurementRequisitionLines.Add(reqLine);
            await seed.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId = tenantId,
                ClientRequestId = Guid.NewGuid(), OrderNumber = "GR-PO-" + suffix,
                CampusId = campus.Id, RequisitionId = requisition.Id, SupplierId = supplier.Id,
                StateCode = "Approved", CreatedBy = makerId,
                SubmittedAt = DateTime.UtcNow.AddMinutes(-5), DecidedAt = DateTime.UtcNow,
                DecidedByUserId = checkerId };
            seed.ProcurementPurchaseOrders.Add(po);
            await seed.SaveChangesAsync(); poId = po.Id;
            var poLine = new ProcurementPurchaseOrderLine { TenantId = tenantId,
                PurchaseOrderId = po.Id, RequisitionLineId = reqLine.Id, LineNo = 1,
                InventoryItemId = item.Id, Quantity = 10m, UnitPrice = 4m };
            seed.ProcurementPurchaseOrderLines.Add(poLine);
            await seed.SaveChangesAsync(); poLineId = poLine.Id;
        }
        CreateProcurementGoodsReceiptRequest Create(decimal quantity, string lot = "LOT-A") => new()
        {
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId, LocationId = storeId,
            Reason = "Received sealed goods against approved purchase order",
            Lines = [new ProcurementGoodsReceiptLineInput {
                PurchaseOrderLineId = poLineId, Quantity = quantity, LotOrBatchNo = lot }]
        };
        async Task<ProcurementGoodsReceiptDto> Submit(decimal quantity, string lot = "LOT-A")
        {
            await using var context = Open(options, tenantId, makerId);
            var service = Goods(context, tenantId, makerId);
            var input = Create(quantity, lot);
            var created = await service.CreateGoodsReceiptAsync(input);
            Assert.True(created.Success, created.Message);
            var replay = await service.CreateGoodsReceiptAsync(input);
            Assert.Equal(created.Data!.Reference, replay.Data!.Reference);
            Assert.Equal(409, (await service.CreateGoodsReceiptAsync(new CreateProcurementGoodsReceiptRequest {
                ClientRequestId = input.ClientRequestId, PurchaseOrderId = poId,
                LocationId = storeId, Reason = input.Reason,
                Lines = [new ProcurementGoodsReceiptLineInput {
                    PurchaseOrderLineId = poLineId, Quantity = quantity + 1m, LotOrBatchNo = lot }]
            })).StatusCode);
            var submitted = await service.SubmitGoodsReceiptAsync(created.Data.Reference,
                new SubmitProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = created.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            return submitted.Data!;
        }
        var first = await Submit(4m);
        await using (var before = Open(options, tenantId, checkerId))
        {
            Assert.Empty(await before.InventoryMovements.ToListAsync());
            Assert.Equal(403, (await Goods(before, tenantId, checkerId).SubmitGoodsReceiptAsync(first.Reference,
                new SubmitProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = first.RowVersion })).StatusCode);
            var accepted = await Goods(before, tenantId, checkerId).ReviewGoodsReceiptAsync(first.Reference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = first.RowVersion, Decision = "Approve", ValuationMode = "Automatic",
                    Reason = "Independent inspection of sealed cartons" });
            Assert.True(accepted.Success, accepted.Message);
            Assert.Equal(4m, Assert.Single(accepted.Data!.Lines).PostedUnitCost);
        }
        // A separate delivery proves the actual maker cannot sign their own acceptance.
        var second = await Submit(3m, "LOT-B");
        await using (var makerContext = Open(options, tenantId, makerId))
            Assert.Equal(403, (await Goods(makerContext, tenantId, makerId).ReviewGoodsReceiptAsync(second.Reference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = second.RowVersion, Decision = "Approve", ValuationMode = "Automatic",
                    Reason = "Maker cannot sign own stock acceptance" })).StatusCode);
        await using (var checkerContext = Open(options, tenantId, checkerId))
        {
            var service = Goods(checkerContext, tenantId, checkerId);
            var approval = new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                RowVersion = second.RowVersion, Decision = "Approve", ValuationMode = "ManualFinal",
                Reason = "Independent stock checker verified invoice valuation",
                Valuations = [new ProcurementGoodsReceiptValuationInput { LineNo = 1, Amount = 4.5m,
                    Reason = "Inspection revealed documented valuation adjustment" }] };
            var accepted = await service.ReviewGoodsReceiptAsync(second.Reference, approval);
            Assert.True(accepted.Success, accepted.Message);
            Assert.Equal("Posted", accepted.Data!.StateCode);
            Assert.NotNull(accepted.Data.PostedMovementReference);
            Assert.Equal(4.5m, Assert.Single(accepted.Data.Lines).PostedUnitCost);
            Assert.Equal(accepted.Data.PostedMovementReference,
                (await service.ReviewGoodsReceiptAsync(second.Reference, approval)).Data!.PostedMovementReference);
            Assert.Equal(409, (await service.ReviewGoodsReceiptAsync(second.Reference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = second.RowVersion, Decision = "Reject", ValuationMode = "Automatic",
                    Reason = "Stale competing checker request" })).StatusCode);
        }
        await using (var check = Open(options, tenantId, checkerId))
        {
            var movements = await check.InventoryMovements.Where(x => x.SourceType == "ProcurementGoodsReceipt").ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.All(movements, m => Assert.Equal("Posted", m.StateCode));
            Assert.Equal(7m, await check.InventoryMovementLines
                .Where(x => movements.Select(m => m.Id).Contains(x.InventoryMovementId)).SumAsync(x => x.Quantity));
            Assert.Equal(409, (await Goods(check, tenantId, checkerId).CreateGoodsReceiptAsync(Create(4m))).StatusCode);
        }
        // A rejected delivery frees its held remaining quantity; no additional stock entry.
        var reject = await Submit(3m, "LOT-C");
        await using (var review = Open(options, tenantId, checkerId))
        {
            var service = Goods(review, tenantId, checkerId);
            Assert.True((await service.ReviewGoodsReceiptAsync(reject.Reference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = reject.RowVersion, Decision = "Reject", ValuationMode = "Automatic",
                    Reason = "Supplier delivered damaged cartons" })).Success);
        }
        var replacement = await Submit(3m, "LOT-C");
        await using (var review = Open(options, tenantId, checkerId))
        {
            var accepted = await Goods(review, tenantId, checkerId).ReviewGoodsReceiptAsync(replacement.Reference,
                new ReviewProcurementGoodsReceiptRequest { ClientRequestId = Guid.NewGuid(),
                    RowVersion = replacement.RowVersion, Decision = "Approve", ValuationMode = "AutoPlusManual",
                    Reason = "Corrected final delivered supplier unit pricing",
                    Valuations = [new ProcurementGoodsReceiptValuationInput { LineNo = 1, Amount = 0.25m,
                        Reason = "Approved extra handling cost evidenced on delivery" }] });
            Assert.True(accepted.Success, accepted.Message);
            Assert.Equal(4.25m, Assert.Single(accepted.Data!.Lines).PostedUnitCost);
        }
        await using (var check = Open(options, tenantId, makerId))
        {
            Assert.Equal(3, await check.InventoryMovements.CountAsync(x => x.SourceType == "ProcurementGoodsReceipt"));
            Assert.Equal(10m, await check.InventoryMovementLines.SumAsync(x => x.Quantity));
            Assert.Equal(409, (await Goods(check, tenantId, makerId).CreateGoodsReceiptAsync(Create(1m))).StatusCode);
            Assert.Equal(409, (await Service(check, tenantId, makerId).TransitionAsync(poId,"request-cancel",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion =
                    (await Service(check, tenantId, makerId).GetAsync(poId)).Data!.RowVersion,
                    Reason = "Should not cancel after posted inventory stock" })).StatusCode);
        }
        await using (var otherTenant = Open(options, tenantId + 99999, makerId))
        {
            Assert.Equal(404, (await Goods(otherTenant, tenantId + 99999, makerId)
                .GetGoodsReceiptAsync(first.Reference)).StatusCode);
            Assert.Empty((await Goods(otherTenant, tenantId + 99999, makerId)
                .GetGoodsReceiptsAsync(null, null, 1, 25)).Data!.Items);
        }
        Assert.True(campusId > 0);
    }
}
