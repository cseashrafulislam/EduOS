using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Enterprise;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerV3EnterpriseProcurementTraceTests
{
    private sealed class Actor(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 1;
        public string? FullName => "Enterprise SQL";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public async Task SQL_procurement_source_integrity_paging_tenant_IDOR_and_historical_tombstone()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantA, tenantB;
        await using (var root = new EduOSDbContext(options))
        {
            var a = new Tenant { Code = "EPT-A-" + suffix, Name = "Enterprise A",
                Email = "ept-a-" + suffix + "@example.test", State = TenantState.Active };
            var b = new Tenant { Code = "EPT-B-" + suffix, Name = "Enterprise B",
                Email = "ept-b-" + suffix + "@example.test", State = TenantState.Active };
            root.Tenants.AddRange(a, b);
            await root.SaveChangesAsync();
            tenantA = a.Id;
            tenantB = b.Id;
        }

        Guid reference;
        long requisitionLineId, orderId, supplierId, purchaseOrderLineId;
        await using (var seed = Open(options, tenantA))
        {
            var campus = new Campus { TenantId = tenantA,
                Code = "EPT-C-" + suffix, Name = "Main campus" };
            var item = new InventoryItem { TenantId = tenantA,
                Code = "EPT-I-" + suffix, Name = "Printer paper",
                IsActive = true, IsStockTracked = true };
            seed.Campuses.Add(campus);
            seed.InventoryItems.Add(item);
            await seed.SaveChangesAsync();
            var req = new ProcurementRequisition { TenantId = tenantA,
                CampusId = campus.Id, PublicId = Guid.NewGuid(),
                ClientRequestId = Guid.NewGuid(), RequestNumber = "EPT-R-" + suffix,
                Purpose = "Office stationery procurement", StateCode = "Approved" };
            var supplier = new ProcurementSupplier { TenantId = tenantA,
                CampusId = campus.Id, PublicId = Guid.NewGuid(),
                ClientRequestId = Guid.NewGuid(), SupplierCode = "EPT-S-" + suffix,
                Name = "Supplier", StateCode = "Approved" };
            seed.ProcurementRequisitions.Add(req);
            seed.ProcurementSuppliers.Add(supplier);
            await seed.SaveChangesAsync();
            var reqLine = new ProcurementRequisitionLine { TenantId = tenantA,
                RequisitionId = req.Id, LineNo = 1, InventoryItemId = item.Id,
                Quantity = 5m, EstimatedUnitPrice = 10m };
            seed.ProcurementRequisitionLines.Add(reqLine);
            await seed.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId = tenantA,
                PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                OrderNumber = "EPT-PO-" + suffix, CampusId = campus.Id,
                RequisitionId = req.Id, SupplierId = supplier.Id,
                StateCode = "Approved", CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedBy = 101, SubmittedAt = DateTime.UtcNow.AddMinutes(-3),
                DecidedAt = DateTime.UtcNow.AddMinutes(-1), DecidedByUserId = 202 };
            seed.ProcurementPurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
            var poLine = new ProcurementPurchaseOrderLine { TenantId = tenantA,
                PurchaseOrderId = po.Id, RequisitionLineId = reqLine.Id,
                LineNo = 1, InventoryItemId = item.Id,
                Quantity = 4m, UnitPrice = 6m };
            seed.ProcurementPurchaseOrderLines.Add(poLine);
            await seed.SaveChangesAsync();
            reference = po.PublicId;
            requisitionLineId = reqLine.Id;
            orderId = po.Id;
            supplierId = supplier.Id;
            purchaseOrderLineId = poLine.Id;
        }

        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        await using (var own = Open(options, tenantA))
        {
            var trace = new V3EnterpriseProcurementTraceService(own, new Actor(tenantA));
            var detail = await trace.GetAsync(reference);
            Assert.True(detail.Success, detail.Message);
            Assert.Equal("ApprovedCommitment", detail.Data!.EvidenceStatus);
            Assert.Equal(24m, detail.Data.TotalAmount);
            Assert.Equal(50m, detail.Data.ApprovedBudget);
            Assert.Equal(26m, detail.Data.BudgetVariance);
            Assert.Equal("BDT", detail.Data.CurrencyCode);
            Assert.Equal(1, detail.Data.LineCount);
            Assert.NotEqual(Guid.Empty, detail.Data.RequisitionReference);
            Assert.NotEqual(Guid.Empty, detail.Data.SupplierReference);
            var report = await trace.RegisterAsync(from, to, null, 1, 25);
            Assert.True(report.Success, report.Message);
            var row = Assert.Single(report.Data!.Items.Where(x => x.Reference == reference));
            Assert.Equal("ApprovedCommitment", row.EvidenceStatus);
            Assert.Equal(detail.Data.TotalAmount, row.RecordedValue);
        }
        await using (var foreign = Open(options, tenantB))
        {
            var trace = new V3EnterpriseProcurementTraceService(foreign, new Actor(tenantB));
            Assert.Equal(404, (await trace.GetAsync(reference)).StatusCode);
            var report = await trace.RegisterAsync(from, to, null, 1, 25);
            Assert.True(report.Success, report.Message);
            Assert.DoesNotContain(report.Data!.Items, x => x.Reference == reference);
        }
        // The register must classify the *actual* maker/checker cancellation
        // lifecycle, not mislabel cancelled or pending-cancellation POs as
        // unapproved. Cancellation requests remain active commitments.
        await using (var lifecycle = Open(options, tenantA))
        {
            var trace = new V3EnterpriseProcurementTraceService(lifecycle, new Actor(tenantA));
            foreach (var (state, expected) in new[] {
                ("CancelRequested", "CancellationPending"),
                ("Cancelled", "CancelledCommitment"),
                ("Rejected", "RejectedCommitment"),
                ("Draft", "DraftCommitment"),
                ("Approved", "ApprovedCommitment")
            })
            {
                await lifecycle.ProcurementPurchaseOrders
                    .Where(x => x.TenantId == tenantA && x.Id == orderId)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(y => y.StateCode, state)
                        .SetProperty(y => y.DecisionReason, "Owner requested cancellation review"));
                var detail = await trace.GetAsync(reference);
                Assert.True(detail.Success, detail.Message);
                Assert.Equal(expected, detail.Data!.EvidenceStatus);
                var page = await trace.RegisterAsync(from, to, null, 1, 25);
                Assert.True(page.Success, page.Message);
                Assert.Equal(expected, Assert.Single(page.Data!.Items
                    .Where(x => x.Reference == reference)).EvidenceStatus);
            }

            // A forged same-person approval, revoked supplier or budget
            // overrun must fail closed in both detail and register exports.
            await lifecycle.ProcurementPurchaseOrders
                .Where(x => x.TenantId == tenantA && x.Id == orderId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.DecidedByUserId, (long?)101));
            Assert.Equal(409, (await trace.GetAsync(reference)).StatusCode);
            await lifecycle.ProcurementPurchaseOrders
                .Where(x => x.TenantId == tenantA && x.Id == orderId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.DecidedByUserId, (long?)202));

            await lifecycle.ProcurementSuppliers
                .Where(x => x.TenantId == tenantA && x.Id == supplierId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.StateCode, "Draft"));
            Assert.Equal(409, (await trace.GetAsync(reference)).StatusCode);
            var supplierConflict = await trace.RegisterAsync(from, to, null, 1, 25);
            Assert.Equal("EvidenceConflict", Assert.Single(supplierConflict.Data!.Items
                .Where(x => x.Reference == reference)).EvidenceStatus);
            await lifecycle.ProcurementSuppliers
                .Where(x => x.TenantId == tenantA && x.Id == supplierId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.StateCode, "Approved"));

            await lifecycle.ProcurementPurchaseOrderLines
                .Where(x => x.TenantId == tenantA && x.Id == purchaseOrderLineId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.UnitPrice, 13m));
            Assert.Equal(409, (await trace.GetAsync(reference)).StatusCode);
            await lifecycle.ProcurementPurchaseOrderLines
                .Where(x => x.TenantId == tenantA && x.Id == purchaseOrderLineId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.UnitPrice, 6m));
            Assert.Equal("ApprovedCommitment", (await trace.GetAsync(reference)).Data!.EvidenceStatus);
        }

        // Disposable SQL-only historical corruption simulation: a purchase order
        // referencing a soft-deleted approved requisition line must fail closed.
        await using (var corrupt = Open(options, tenantA))
        {
            await corrupt.ProcurementRequisitionLines
                .Where(x => x.TenantId == tenantA && x.Id == requisitionLineId)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.IsDeleted, true));
        }
        await using (var verify = Open(options, tenantA))
        {
            var trace = new V3EnterpriseProcurementTraceService(verify, new Actor(tenantA));
            Assert.Equal(409, (await trace.GetAsync(reference)).StatusCode);
            var report = await trace.RegisterAsync(from, to, null, 1, 25);
            Assert.True(report.Success, report.Message);
            Assert.Equal("EvidenceConflict", Assert.Single(
                report.Data!.Items.Where(x => x.Reference == reference)).EvidenceStatus);
        }
    }
}
