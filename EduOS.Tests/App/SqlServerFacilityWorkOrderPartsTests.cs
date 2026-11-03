using EduOS.Core.DTOs.Facilities;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerFacilityWorkOrderTestsParts
{
    [Fact]
    public async Task Facility_parts_use_canonical_ledger_with_campus_idempotency_independent_post_and_reversal()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, campusId, requestId, stockLocationId, itemId, makerId, checkerId, managerId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-PART-" + suffix, Name = "Parts CI",
                Email = "parts-" + suffix + "@example.test", State = TenantState.Active };
            var actors = Enumerable.Range(1, 3).Select(i => {
                var email = $"parts-{i}-{suffix}@example.test";
                return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = "Parts CI Actor",
                    EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.Add(tenant); setup.AddRange(actors);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; makerId = actors[0].Id; checkerId = actors[1].Id; managerId = actors[2].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "PART-" + suffix, Name = "Parts Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "SPACE-" + suffix, Name = "CI Maintenance Space" };
                var location = new InventoryLocation { TenantId = tenantId, CampusId = campusId,
                    Code = "LOC-" + suffix, Name = "CI Stock", IsActive = true };
                var item = new InventoryItem { TenantId = tenantId, Code = "PART-" + suffix,
                    Name = "Replacement Filter", IsActive = true, IsStockTracked = true, UnitCode = "Each" };
                setup.AddRange(space, location, item);
                setup.UserCampusAccesses.AddRange(new UserCampusAccess {
                    TenantId = tenantId, CampusId = campusId, UserId = checkerId, IsActive = true
                }, new UserCampusAccess {
                    TenantId = tenantId, CampusId = campusId, UserId = managerId, IsActive = true
                });
                await setup.SaveChangesAsync();
                stockLocationId = location.Id; itemId = item.Id;
                var facility = new FacilityServiceRequest { TenantId = tenantId,
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId, FacilitySpaceId = space.Id,
                    RequesterUserId = managerId, CategoryCode = "Repair", PriorityCode = "Normal",
                    Summary = "Replace worn-out ventilation filter", StateCode = "Approved",
                    ApprovedByUserId = checkerId, ApprovedAtUtc = DateTime.UtcNow };
                setup.FacilityServiceRequests.Add(facility);
                await setup.SaveChangesAsync();
                requestId = facility.Id;
                setup.FacilityWorkOrders.Add(new FacilityWorkOrder { TenantId = tenantId,
                    FacilityServiceRequestId = requestId, StateCode = "InProgress" });
                var receipt = new InventoryMovement { TenantId = tenantId,
                    PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                    MovementNumber = "IM-PART-" + suffix, MovementTypeCode = "Receipt",
                    SourceType = "ManualReceipt", ToLocationId = location.Id,
                    StateCode = "Posted", MovementAt = DateTime.UtcNow,
                    PostedAt = DateTime.UtcNow, PostedByUserId = checkerId, Remarks = "Parts test stock intake" };
                setup.InventoryMovements.Add(receipt);
                await setup.SaveChangesAsync();
                setup.InventoryMovementLines.Add(new InventoryMovementLine { TenantId = tenantId,
                    InventoryMovementId = receipt.Id, LineNo = 1, InventoryItemId = item.Id,
                    Quantity = 10m, UnitCost = 12.5m, LotOrBatchNo = "LOT-CI" });
                await setup.SaveChangesAsync();
            }
        }

        InventoryMovementService Inventory(EduOSDbContext db, long actorId, string role) =>
            new(db, new Principal(tenantId, actorId, role), TimeProvider.System,
                NullLogger<InventoryMovementService>.Instance);

        var input = new CreateFacilityPartsIssueDto { ClientRequestId = Guid.NewGuid(),
            FromLocationId = stockLocationId, Reason = "Filter replacement for approved work order",
            Lines = [new FacilityPartsIssueLineInputDto {
                InventoryItemId = itemId, Quantity = 2m, LotOrBatchNo = "LOT-CI" }] };
        await using (var unauthorized = Db(options, tenantId, makerId, "StoreKeeper"))
            Assert.Equal(403, (await Inventory(unauthorized, makerId, "StoreKeeper")
                .CreateFacilityPartsIssueAsync(requestId, input)).StatusCode);
        await using (var grant = Db(options, tenantId, managerId, "Principal"))
        {
            grant.UserCampusAccesses.Add(new UserCampusAccess {
                TenantId = tenantId, CampusId = campusId, UserId = makerId, IsActive = true });
            await grant.SaveChangesAsync();
        }
        Guid reference; string rowVersion;
        await using (var makerDb = Db(options, tenantId, makerId, "StoreKeeper"))
        {
            var service = Inventory(makerDb, makerId, "StoreKeeper");
            var create = await service.CreateFacilityPartsIssueAsync(requestId, input);
            Assert.True(create.Success, create.Message);
            Assert.Equal("FacilityWorkOrder", create.Data!.SourceType);
            Assert.Equal("Draft", create.Data.StateCode);
            reference = create.Data.Reference; rowVersion = create.Data.RowVersion;
            var replay = await service.CreateFacilityPartsIssueAsync(requestId, input);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(reference, replay.Data!.Reference);
            input.Reason = "This request ID has changed its original meaning";
            Assert.Equal(409, (await service.CreateFacilityPartsIssueAsync(requestId, input)).StatusCode);
            input.Reason = "Filter replacement for approved work order";
            Assert.Equal(403, (await service.PostMovementAsync(reference,
                new PostInventoryMovementRequestDto { RowVersion = rowVersion })).StatusCode);
        }
        await using (var checkerDb = Db(options, tenantId, checkerId, "InventoryManager"))
        {
            var service = Inventory(checkerDb, checkerId, "InventoryManager");
            var posted = await service.PostMovementAsync(reference, new PostInventoryMovementRequestDto { RowVersion = rowVersion });
            Assert.True(posted.Success, posted.Message);
            Assert.Equal("Posted", posted.Data!.StateCode);
            Assert.Equal(12.5m, Assert.Single(posted.Data.Lines).UnitCost);
        }
        await using (var managerDb = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var report = await Service(managerDb, tenantId, managerId, "FacilitiesManager")
                .PartsAsync(requestId, 1, 25);
            Assert.True(report.Success, report.Message);
            var line = Assert.Single(report.Data!.Items);
            Assert.Equal("Posted", line.StateCode);
            Assert.Equal("Unlinked", line.FinanceState);
            Assert.Equal(25m, line.GrossCost);
            Assert.Equal(25m, line.NetCost);
        }
        await using (var adminDb = Db(options, tenantId, managerId, "Principal"))
        {
            var service = Inventory(adminDb, managerId, "Principal");
            var before = await service.GetMovementAsync(reference);
            var reversed = await service.ReverseMovementAsync(reference,
                new ReverseInventoryMovementRequestDto { ClientRequestId = Guid.NewGuid(),
                    RowVersion = before.Data!.RowVersion, Reason = "Defective parts returned to stock" });
            Assert.True(reversed.Success, reversed.Message);
            var stock = await service.GetItemBalanceAsync(itemId, stockLocationId, null);
            Assert.True(stock.Success, stock.Message);
            Assert.Equal(10m, stock.Data!.QuantityOnHand);
            var report = await Service(adminDb, tenantId, managerId, "Principal").PartsAsync(requestId, 1, 25);
            var line = Assert.Single(report.Data!.Items);
            Assert.Equal("Reversed", line.StateCode);
            Assert.Equal(25m, line.GrossCost);
            Assert.Equal(0m, line.NetCost);
            Assert.NotNull(line.ReversalReference);
        }
        await using (var makerDb = Db(options, tenantId, makerId, "StoreKeeper"))
        {
            input.ClientRequestId = Guid.NewGuid();
            var service = Inventory(makerDb, makerId, "StoreKeeper");
            var unused = await service.CreateFacilityPartsIssueAsync(requestId, input);
            Assert.True(unused.Success, unused.Message);
            var voided = await service.VoidFacilityPartsIssueAsync(unused.Data!.Reference,
                new VoidFacilityPartsIssueDto { RowVersion = unused.Data.RowVersion,
                    Reason = "Requested parts were no longer necessary" });
            Assert.True(voided.Success, voided.Message);
            Assert.Equal("Voided", voided.Data!.StateCode);
            await using var checkerDb = Db(options, tenantId, checkerId, "InventoryManager");
            Assert.Equal(409, (await Inventory(checkerDb, checkerId, "InventoryManager")
                .PostMovementAsync(voided.Data.Reference,
                    new PostInventoryMovementRequestDto { RowVersion = voided.Data.RowVersion })).StatusCode);
        }
        await using (var reportDb = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var report = await Service(reportDb, tenantId, managerId, "FacilitiesManager")
                .PartsAsync(requestId, 1, 25);
            Assert.Equal(2, report.Data!.TotalCount);
            Assert.Contains(report.Data.Items, x => x.StateCode == "Voided" && x.NetCost == 0m);
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                 new Claim(ClaimTypes.Role, role)], "Test"))
        };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static FacilityWorkflowService Service(EduOSDbContext db, long tenantId, long userId, string role) =>
        new(db, new Principal(tenantId, userId, role), TimeProvider.System,
            NullLogger<FacilityWorkflowService>.Instance);

    private sealed class Principal(long tenant, long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string candidate) => candidate == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
