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
public sealed class SqlServerV3ProcurementRequisitionTests
{
    private sealed class Actor(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => userId;
        public string? FullName => "Procurement SQL";
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
    private static ProcurementRequisitionService Service(EduOSDbContext db, long tenant, long userId)
        => new(db, new Actor(tenant, userId), TimeProvider.System,
            NullLogger<ProcurementRequisitionService>.Instance);

    [Fact]
    public async Task SQL_unique_request_and_competing_decisions_preserve_one_approved_request()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, makerId, checkerId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "PROC-" + suffix, Name = "Procurement SQL",
                Email = "proc-" + suffix + "@example.test", State = TenantState.Active };
            var maker = new ApplicationUser { UserName = "proc-m-" + suffix,
                NormalizedUserName = ("proc-m-" + suffix).ToUpperInvariant(),
                Email = "proc-m-" + suffix + "@example.test",
                NormalizedEmail = ("proc-m-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Maker" };
            var checker = new ApplicationUser { UserName = "proc-c-" + suffix,
                NormalizedUserName = ("proc-c-" + suffix).ToUpperInvariant(),
                Email = "proc-c-" + suffix + "@example.test",
                NormalizedEmail = ("proc-c-" + suffix + "@example.test").ToUpperInvariant(), FullName = "Checker" };
            root.Tenants.Add(tenant); root.ApplicationUsers.AddRange(maker, checker);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; makerId = maker.Id; checkerId = checker.Id;
        }
        long campusId, itemId;
        await using (var seed = Open(options, tenantId))
        {
            var campus = new Campus { TenantId = tenantId, Code = "C-" + suffix, Name = "Campus" };
            var item = new InventoryItem { TenantId = tenantId, Code = "I-" + suffix, Name = "Stock item" };
            seed.Campuses.Add(campus); seed.InventoryItems.Add(item);
            await seed.SaveChangesAsync(); campusId = campus.Id; itemId = item.Id;
        }
        var request = new SaveProcurementRequisitionRequest
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId, Purpose = "Approved stationery requisition",
            Lines = [new ProcurementRequisitionLineInput { InventoryItemId = itemId, Quantity = 3, EstimatedUnitPrice = 2 }]
        };
        long id;
        await using (var makerDb = Open(options, tenantId))
        {
            var maker = Service(makerDb, tenantId, makerId);
            var created = await maker.SaveAsync(null, request);
            Assert.True(created.Success, created.Message);
            id = created.Data!.Id;
            var replay = await maker.SaveAsync(null, request);
            Assert.Equal(id, replay.Data!.Id);
            var submitted = await maker.TransitionAsync(id, "submit",
                new ProcurementDecisionRequest { RowVersion = created.Data.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            var denied = await maker.TransitionAsync(id, "approve",
                new ProcurementDecisionRequest { RowVersion = submitted.Data!.RowVersion });
            Assert.Equal(403, denied.StatusCode);
        }
        await using var firstDb = Open(options, tenantId);
        await using var secondDb = Open(options, tenantId);
        var first = Service(firstDb, tenantId, checkerId);
        var second = Service(secondDb, tenantId, checkerId);
        var snapshot = await first.GetAsync(id);
        var version = snapshot.Data!.RowVersion;
        var accepted = await first.TransitionAsync(id, "approve",
            new ProcurementDecisionRequest { RowVersion = version });
        Assert.True(accepted.Success, accepted.Message);
        var competing = await second.TransitionAsync(id, "reject",
            new ProcurementDecisionRequest { RowVersion = version, Reason = "Competing rejection" });
        Assert.Equal(409, competing.StatusCode);
        await using var verify = Open(options, tenantId);
        Assert.Equal("Approved", (await Service(verify, tenantId, makerId).GetAsync(id)).Data!.StateCode);
        Assert.Equal(1, await verify.ProcurementRequisitions.CountAsync(x => x.Id == id));
        Assert.Empty(await verify.InventoryMovements.Where(x => x.SourceType == "ProcurementRequisition").ToListAsync());
    }
}
