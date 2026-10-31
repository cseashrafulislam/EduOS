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
public sealed class SqlServerV3ProcurementSupplierAcknowledgementTests
{
    private sealed class Actor(long tenant, long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => id;
        public string? FullName => "Supplier response SQL";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant, long userId)
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = tenant;
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
             new System.Security.Claims.Claim("TenantId", tenant.ToString())], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ProcurementSupplierAcknowledgementService Service(
        EduOSDbContext db, long tenant, long id)
        => new(db, new Actor(tenant, id), TimeProvider.System,
            NullLogger<ProcurementSupplierAcknowledgementService>.Instance);

    [Fact]
    public async Task SQL_supplier_response_replay_checker_cancellation_race_and_tenant_IDOR()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId, poId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "ACK-" + suffix, Name = "Supplier response SQL",
                Email = "ack-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "ack-m-" + suffix,
                NormalizedUserName = ("ack-m-" + suffix).ToUpperInvariant(),
                Email = "ack-m-" + suffix + "@example.test",
                NormalizedEmail = ("ack-m-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Maker" };
            var checker = new ApplicationUser { UserName = "ack-c-" + suffix,
                NormalizedUserName = ("ack-c-" + suffix).ToUpperInvariant(),
                Email = "ack-c-" + suffix + "@example.test",
                NormalizedEmail = ("ack-c-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Checker" };
            root.Tenants.Add(tenant); root.ApplicationUsers.AddRange(maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        await using (var seed = Open(options, tenantId, makerId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "C-" + suffix, Name = "Campus" };
            seed.Campuses.Add(campus);
            await seed.SaveChangesAsync();
            var req = new ProcurementRequisition { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), RequestNumber = "PR-" + suffix,
                Purpose = "Procure office supplies for new rooms", StateCode = "Approved", CreatedBy = makerId };
            var supplier = new ProcurementSupplier { TenantId = tenantId, CampusId = campus.Id,
                ClientRequestId = Guid.NewGuid(), SupplierCode = "S-" + suffix,
                Name = "Stationery supplier", StateCode = "Approved", CreatedBy = makerId };
            seed.ProcurementRequisitions.Add(req); seed.ProcurementSuppliers.Add(supplier);
            await seed.SaveChangesAsync();
            var po = new ProcurementPurchaseOrder { TenantId = tenantId, CampusId = campus.Id,
                RequisitionId = req.Id, SupplierId = supplier.Id, ClientRequestId = Guid.NewGuid(),
                OrderNumber = "PO-" + suffix, StateCode = "Approved", CreatedBy = makerId };
            seed.ProcurementPurchaseOrders.Add(po);
            await seed.SaveChangesAsync(); poId = po.Id;
        }
        var input = new SaveProcurementSupplierAcknowledgementRequest
        {
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId,
            SupplierReference = "SUP-ACK-" + suffix, ResponseDate = DateTime.UtcNow.Date,
            PromisedDeliveryDate = DateTime.UtcNow.Date.AddDays(7)
        };
        long ackId; string version;
        await using (var makerDb = Open(options, tenantId, makerId))
        {
            var maker = Service(makerDb, tenantId, makerId);
            var created = await maker.SaveAsync(null, input);
            Assert.True(created.Success, created.Message);
            ackId = created.Data!.Id;
            Assert.Equal(ackId, (await maker.SaveAsync(null, input)).Data!.Id);
            Assert.Equal(409, (await maker.SaveAsync(null, new SaveProcurementSupplierAcknowledgementRequest
            {
                ClientRequestId = Guid.NewGuid(), PurchaseOrderId = poId,
                SupplierReference = input.SupplierReference, ResponseDate = input.ResponseDate,
                PromisedDeliveryDate = input.PromisedDeliveryDate
            })).StatusCode);
            var submitted = await maker.TransitionAsync(ackId, "submit",
                new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = created.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message); version = submitted.Data!.RowVersion;
            Assert.Equal(403, (await maker.TransitionAsync(ackId, "approve",
                new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = version })).StatusCode);
        }
        // An approved order withdrawn while checker is deciding must not gain a false confirmation.
        await using (var cancel = Open(options, tenantId, makerId))
        {
            var po = await cancel.ProcurementPurchaseOrders.SingleAsync(x => x.Id == poId);
            po.StateCode = "CancelRequested"; await cancel.SaveChangesAsync();
        }
        await using (var blocked = Open(options, tenantId, checkerId))
            Assert.Equal(409, (await Service(blocked, tenantId, checkerId).TransitionAsync(ackId, "approve",
                new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = version })).StatusCode);
        await using (var restore = Open(options, tenantId, makerId))
        {
            var po = await restore.ProcurementPurchaseOrders.SingleAsync(x => x.Id == poId);
            po.StateCode = "Approved"; await restore.SaveChangesAsync();
        }
        await using var checkerDb = Open(options, tenantId, checkerId);
        await using var staleDb = Open(options, tenantId, checkerId);
        // Competing independent checker decisions must produce exactly one committed result.
        var competing = await Task.WhenAll(
            Service(checkerDb, tenantId, checkerId).TransitionAsync(ackId, "approve",
                new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = version }),
            Service(staleDb, tenantId, checkerId).TransitionAsync(ackId, "approve",
                new ProcurementSupplierAcknowledgementDecisionRequest { RowVersion = version }));
        Assert.Single(competing.Where(x => x.Success));
        Assert.Single(competing.Where(x => x.StatusCode == 409));
        Assert.Equal("Confirmed", competing.Single(x => x.Success).Data!.StateCode);
        await using (var report = Open(options, tenantId, checkerId))
        {
            var commitments = await new ProcurementPurchaseOrderService(
                report, new Actor(tenantId, checkerId), TimeProvider.System,
                NullLogger<ProcurementPurchaseOrderService>.Instance)
                .CommitmentsAsync(1, 25, null, null, null, false, null);
            Assert.True(commitments.Success, commitments.Message);
            var commitment = Assert.Single(commitments.Data!.Items.Where(x => x.Id == poId));
            Assert.Equal(input.PromisedDeliveryDate, commitment.SupplierPromisedDeliveryDate);
            Assert.Equal(input.SupplierReference, commitment.SupplierConfirmationReference);
            Assert.NotNull(commitment.SupplierConfirmedAt);
        }
        await using (var verify = Open(options, tenantId, makerId))
        {
            Assert.Equal(1, await verify.ProcurementSupplierAcknowledgements.CountAsync(x => x.Id == ackId));
            Assert.Empty(await verify.InventoryMovements.ToListAsync());
            Assert.Equal("Confirmed", (await Service(verify, tenantId, makerId).GetAsync(ackId)).Data!.StateCode);
        }
        await using var foreign = Open(options, tenantId + 1, checkerId);
        Assert.Equal(404, (await Service(foreign, tenantId + 1, checkerId).GetAsync(ackId)).StatusCode);
    }
}
