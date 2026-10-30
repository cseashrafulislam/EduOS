using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerV3ProcurementPurchaseOrderTests
{
    private sealed class Actor(long tenant, long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => id;
        public string? FullName => "PO SQL";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant, long userId = 0)
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = tenant;
        if (userId > 0) http.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
             new System.Security.Claims.Claim("TenantId", tenant.ToString())], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ProcurementPurchaseOrderService Service(EduOSDbContext db, long tenant, long id)
        => new(db, new Actor(tenant, id), TimeProvider.System,
            NullLogger<ProcurementPurchaseOrderService>.Instance);
    [Fact]
    public async Task SQL_approved_PO_replay_competing_checker_and_tenant_isolation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "PO-" + suffix, Name = "PO SQL",
                Email = "po-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "po-m-" + suffix,
                NormalizedUserName = ("po-m-" + suffix).ToUpperInvariant(),
                Email = "po-m-" + suffix + "@example.test",
                NormalizedEmail = ("po-m-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Maker" };
            var checker = new ApplicationUser { UserName = "po-c-" + suffix,
                NormalizedUserName = ("po-c-" + suffix).ToUpperInvariant(),
                Email = "po-c-" + suffix + "@example.test",
                NormalizedEmail = ("po-c-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Checker" };
            root.Tenants.Add(tenant); root.ApplicationUsers.AddRange(maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        long reqId, supplierId;
        await using (var seed = Open(options, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "C-" + suffix, Name = "Campus" };
            var item = new InventoryItem { TenantId = tenantId, Code = "I-" + suffix, Name = "Pens",
                IsActive = true, IsStockTracked = true };
            seed.Campuses.Add(campus); seed.InventoryItems.Add(item);
            await seed.SaveChangesAsync();
            var req = new ProcurementRequisition { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), RequestNumber = "PR-" + suffix,
                Purpose = "Purchase new office stationery", StateCode = "Approved", CreatedBy = makerId };
            var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), SupplierCode = "S-" + suffix, Name = "Office supplier",
                StateCode = "Approved", CreatedBy = makerId };
            seed.ProcurementRequisitions.Add(req); seed.ProcurementSuppliers.Add(supplier);
            await seed.SaveChangesAsync(); reqId = req.Id; supplierId = supplier.Id;
            seed.ProcurementRequisitionLines.Add(new ProcurementRequisitionLine {
                TenantId = tenantId, RequisitionId = reqId, LineNo = 1,
                InventoryItemId = item.Id, Quantity = 12m, EstimatedUnitPrice = 5m });
            await seed.SaveChangesAsync();
        }
        var input = new CreateProcurementPurchaseOrderRequest {
            ClientRequestId = Guid.NewGuid(), RequisitionId = reqId, SupplierId = supplierId };
        long id; string version;
        await using (var makerDb = Open(options, tenantId, makerId))
        {
            var maker = Service(makerDb, tenantId, makerId);
            var created = await maker.CreateAsync(input);
            Assert.True(created.Success, created.Message);
            id = created.Data!.Id;
            Assert.Equal(60m, created.Data.TotalAmount);
            Assert.Equal(id, (await maker.CreateAsync(input)).Data!.Id);
            Assert.Equal(409, (await maker.CreateAsync(new CreateProcurementPurchaseOrderRequest {
                ClientRequestId = Guid.NewGuid(), RequisitionId = reqId, SupplierId = supplierId })).StatusCode);
            var amendment = await maker.SaveDraftAsync(id, new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = created.Data.RowVersion, Reason = "Negotiated supplier unit price",
                Lines = [new ProcurementPurchaseOrderDraftLineInput {
                    RequisitionLineId = created.Data.Lines[0].RequisitionLineId,
                    Quantity = 10m, UnitPrice = 4m }]
            });
            Assert.True(amendment.Success, amendment.Message);
            Assert.Equal(40m, amendment.Data!.TotalAmount);
            Assert.Equal(409, (await maker.SaveDraftAsync(id, new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = created.Data.RowVersion, Reason = "Stale supplier negotiation",
                Lines = [new ProcurementPurchaseOrderDraftLineInput {
                    RequisitionLineId = created.Data.Lines[0].RequisitionLineId,
                    Quantity = 10m, UnitPrice = 4m }]
            })).StatusCode);
            Assert.Equal(409, (await maker.SaveDraftAsync(id, new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = amendment.Data.RowVersion, Reason = "Over approved requisition budget",
                Lines = [new ProcurementPurchaseOrderDraftLineInput {
                    RequisitionLineId = created.Data.Lines[0].RequisitionLineId,
                    Quantity = 12m, UnitPrice = 6m }]
            })).StatusCode);
            var submitted = await maker.TransitionAsync(id, "submit",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = amendment.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message); version = submitted.Data!.RowVersion;
            Assert.Equal(403, (await maker.TransitionAsync(id, "approve",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = version })).StatusCode);
        }
        await using var checkerDb = Open(options, tenantId, checkerId);
        await using var competingDb = Open(options, tenantId, checkerId);
        await using (var makerQueueDb = Open(options, tenantId, makerId))
        {
            var ownQueue = await Service(makerQueueDb, tenantId, makerId)
                .PendingApprovalsAsync(1, 20, null, null);
            Assert.True(ownQueue.Success, ownQueue.Message);
            Assert.DoesNotContain(ownQueue.Data!.Items, x => x.Id == id);
        }
        // Revalidate PO/source integrity at checker time, not only when the draft was saved.
        // Simulate a stale line, a reduced approved budget and a disabled stock item.
        await using (var corrupt = Open(options, tenantId, makerId))
        {
            var line = await corrupt.ProcurementPurchaseOrderLines.SingleAsync(x => x.PurchaseOrderId == id);
            line.Quantity = 13m;
            await corrupt.SaveChangesAsync();
        }
        await using (var invalid = Open(options, tenantId, checkerId))
            Assert.Equal(409, (await Service(invalid, tenantId, checkerId).TransitionAsync(id, "approve",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = version })).StatusCode);
        await using (var repair = Open(options, tenantId, makerId))
        {
            var line = await repair.ProcurementPurchaseOrderLines.SingleAsync(x => x.PurchaseOrderId == id);
            line.Quantity = 10m;
            var source = await repair.ProcurementRequisitionLines.SingleAsync(x => x.RequisitionId == reqId);
            source.EstimatedUnitPrice = 3m; // approved budget now 36, PO commitment 40
            await repair.SaveChangesAsync();
        }
        await using (var invalid = Open(options, tenantId, checkerId))
            Assert.Equal(409, (await Service(invalid, tenantId, checkerId).TransitionAsync(id, "approve",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = version })).StatusCode);
        await using (var repair = Open(options, tenantId, makerId))
        {
            var source = await repair.ProcurementRequisitionLines.SingleAsync(x => x.RequisitionId == reqId);
            source.EstimatedUnitPrice = 5m;
            var item = await repair.InventoryItems.SingleAsync(x => x.Id == source.InventoryItemId);
            item.IsActive = false;
            await repair.SaveChangesAsync();
        }
        await using (var invalid = Open(options, tenantId, checkerId))
            Assert.Equal(409, (await Service(invalid, tenantId, checkerId).TransitionAsync(id, "approve",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = version })).StatusCode);
        await using (var repair = Open(options, tenantId, makerId))
        {
            var item = await repair.InventoryItems.SingleAsync(x => x.Code == "I-" + suffix);
            item.IsActive = true;
            await repair.SaveChangesAsync();
        }
        var checkerQueue = await Service(checkerDb, tenantId, checkerId)
            .PendingApprovalsAsync(1, 20, null, null);
        Assert.True(checkerQueue.Success, checkerQueue.Message);
        Assert.Contains(checkerQueue.Data!.Items, x => x.Id == id);
        var approved = await Service(checkerDb, tenantId, checkerId).TransitionAsync(id, "approve",
            new ProcurementPurchaseOrderDecisionRequest { RowVersion = version });
        Assert.True(approved.Success, approved.Message);
        var stale = await Service(competingDb, tenantId, checkerId).TransitionAsync(id, "reject",
            new ProcurementPurchaseOrderDecisionRequest { RowVersion = version, Reason = "Competing" });
        Assert.Equal(409, stale.StatusCode);
        await using var verify = Open(options, tenantId, makerId);
        var afterApproval = await Service(verify, tenantId, makerId)
            .PendingApprovalsAsync(1, 20, null, null);
        Assert.DoesNotContain(afterApproval.Data!.Items, x => x.Id == id);
        var history = await Service(verify, tenantId, makerId).HistoryAsync(id, 1, 20);
        Assert.True(history.Success, history.Message);
        Assert.True(history.Data!.TotalCount >= 1);
        Assert.All(history.Data.Items, x => Assert.True(x.Reason == null || x.Reason.Length <= 500));
        Assert.Equal("Approved", (await Service(verify, tenantId, makerId).GetAsync(id)).Data!.StateCode);
        var commitment = await Service(verify, tenantId, makerId)
            .CommitmentsAsync(1, 25, null, supplierId, DateTime.UtcNow.Date, false, "Office supplier");
        Assert.True(commitment.Success, commitment.Message);
        var row = Assert.Single(commitment.Data!.Items);
        Assert.Equal(id, row.Id);
        Assert.Equal(40m, row.ApprovedAmount);
        Assert.Equal(1, row.LineCount);
        Assert.Equal("Unscheduled", row.DeliverySchedule);
        Assert.Equal(0, row.DaysOverdue);
        Assert.Empty((await Service(verify, tenantId, makerId)
            .CommitmentsAsync(1, 25, null, null, DateTime.UtcNow.Date, true, null)).Data!.Items);
        Assert.Equal(400, (await Service(verify, tenantId, makerId)
            .CommitmentsAsync(0, 25, null, null, null, false, null)).StatusCode);
        Assert.Equal(40m, (await Service(verify, tenantId, makerId).GetAsync(id)).Data!.TotalAmount);
        Assert.Equal(409, (await Service(verify, tenantId, makerId).SaveDraftAsync(id,
            new SaveProcurementPurchaseOrderDraftRequest {
                RowVersion = approved.Data!.RowVersion, Reason = "Attempt to rewrite approved order",
                Lines = [new ProcurementPurchaseOrderDraftLineInput {
                    RequisitionLineId = 1, Quantity = 1m, UnitPrice = 1m }]
            })).StatusCode);
        Assert.Equal(1, await verify.ProcurementPurchaseOrders.CountAsync(x => x.Id == id));
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
        await using var foreign = Open(options, tenantId + 99999, makerId);
        Assert.Equal(404, (await Service(foreign, tenantId + 99999, makerId).GetAsync(id)).StatusCode);
        Assert.Equal(404, (await Service(foreign, tenantId + 99999, makerId)
            .HistoryAsync(id, 1, 20)).StatusCode);
        Assert.DoesNotContain((await Service(foreign, tenantId + 99999, makerId)
            .PendingApprovalsAsync(1, 20, null, null)).Data!.Items, x => x.Id == id);
        Assert.Empty((await Service(foreign, tenantId + 99999, makerId)
            .CommitmentsAsync(1, 25, null, null, null, false, null)).Data!.Items);
    }
}
