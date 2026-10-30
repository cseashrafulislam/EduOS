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
            var submitted = await maker.TransitionAsync(id, "submit",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = created.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message); version = submitted.Data!.RowVersion;
            Assert.Equal(403, (await maker.TransitionAsync(id, "approve",
                new ProcurementPurchaseOrderDecisionRequest { RowVersion = version })).StatusCode);
        }
        await using var checkerDb = Open(options, tenantId, checkerId);
        await using var competingDb = Open(options, tenantId, checkerId);
        var approved = await Service(checkerDb, tenantId, checkerId).TransitionAsync(id, "approve",
            new ProcurementPurchaseOrderDecisionRequest { RowVersion = version });
        Assert.True(approved.Success, approved.Message);
        var stale = await Service(competingDb, tenantId, checkerId).TransitionAsync(id, "reject",
            new ProcurementPurchaseOrderDecisionRequest { RowVersion = version, Reason = "Competing" });
        Assert.Equal(409, stale.StatusCode);
        await using var verify = Open(options, tenantId, makerId);
        Assert.Equal("Approved", (await Service(verify, tenantId, makerId).GetAsync(id)).Data!.StateCode);
        Assert.Equal(1, await verify.ProcurementPurchaseOrders.CountAsync(x => x.Id == id));
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
        await using var foreign = Open(options, tenantId + 99999, makerId);
        Assert.Equal(404, (await Service(foreign, tenantId + 99999, makerId).GetAsync(id)).StatusCode);
    }
}
