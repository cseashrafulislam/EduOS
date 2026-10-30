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
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerV3InventoryTransferLifecycleTests
{
    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Inventory SQL tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed record Fixture(DbContextOptions<EduOSDbContext> Options, long TenantId,
        long UserId, long SourceId, long DestinationId, long ItemId);

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "0")], "SqlFixture"))
        };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static InventoryMovementService Service(EduOSDbContext db, Fixture f,
        string role = "TenantAdmin", long? tenantId = null) =>
        new(db, new Actor(tenantId ?? f.TenantId, f.UserId, role),
            TimeProvider.System, NullLogger<InventoryMovementService>.Instance);

    private static async Task<Fixture> SeedAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, userId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "INV-TR-" + suffix, Name = "Inventory transfer SQL",
                Email = "inv-" + suffix + "@example.test", State = TenantState.Active
            };
            var account = new ApplicationUser
            {
                UserName = "inventory-" + suffix, NormalizedUserName = ("inventory-" + suffix).ToUpperInvariant(),
                Email = "inventory-" + suffix + "@example.test",
                NormalizedEmail = ("inventory-" + suffix + "@example.test").ToUpperInvariant(),
                FullName = "Inventory test operator"
            };
            root.Tenants.Add(tenant);
            root.ApplicationUsers.Add(account);
            await root.SaveChangesAsync();
            tenantId = tenant.Id;
            userId = account.Id;
        }

        long sourceId, destinationId, itemId;
        await using (var db = Open(options, tenantId))
        {
            var origin = new InventoryLocation { TenantId = tenantId, Code = "FROM-" + suffix, Name = "Origin" };
            var destination = new InventoryLocation { TenantId = tenantId, Code = "TO-" + suffix, Name = "Destination" };
            var item = new InventoryItem { TenantId = tenantId, Code = "PEN-" + suffix, Name = "Pens" };
            db.InventoryLocations.AddRange(origin, destination);
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();
            sourceId = origin.Id; destinationId = destination.Id; itemId = item.Id;
            var opening = new InventoryMovement
            {
                TenantId = tenantId, ClientRequestId = Guid.NewGuid(), MovementNumber = "IM-" + Guid.NewGuid().ToString("N"),
                MovementTypeCode = "Receipt", SourceType = "OpeningBalance", StateCode = "Posted",
                ToLocationId = origin.Id, PostedAt = DateTime.UtcNow.AddMinutes(-1),
                MovementAt = DateTime.UtcNow.AddMinutes(-1), PostedByUserId = userId
            };
            db.InventoryMovements.Add(opening);
            await db.SaveChangesAsync();
            db.InventoryMovementLines.Add(new InventoryMovementLine
            {
                TenantId = tenantId, InventoryMovementId = opening.Id, LineNo = 1,
                InventoryItemId = item.Id, Quantity = 10m, UnitCost = 5m
            });
            await db.SaveChangesAsync();
        }
        return new Fixture(options, tenantId, userId, sourceId, destinationId, itemId);
    }

    private static async Task<InventoryMovementDto> DispatchAsync(EduOSDbContext db, Fixture f)
    {
        var service = Service(db, f);
        var draft = await service.CreateMovementAsync(new CreateInventoryMovementRequestDto
        {
            ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Issue",
            SourceType = "TransferDispatch", FromLocationId = f.SourceId,
            ToLocationId = f.DestinationId, Remarks = "Approved warehouse transfer",
            Lines = [new CreateInventoryMovementLineRequestDto
            {
                InventoryItemId = f.ItemId, Quantity = 6m, UnitCost = 0m
            }]
        });
        Assert.Equal(200, draft.StatusCode);
        var posted = await service.PostMovementAsync(draft.Data!.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.Data.RowVersion });
        Assert.Equal(200, posted.StatusCode);
        return posted.Data!;
    }

    [Fact]
    public async Task Dispatch_receive_and_compensations_preserve_one_ledger_and_in_transit_balance()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var dispatch = await DispatchAsync(db, f);
        Assert.Equal("TransferDispatch", dispatch.SourceType);
        Assert.Equal(4m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        Assert.Equal(0m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        var request = new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() };
        var received = await service.ReceiveTransferAsync(dispatch.Reference, request);
        Assert.Equal(200, received.StatusCode);
        Assert.Equal("TransferReceipt", received.Data!.SourceType);
        Assert.Equal(6m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        Assert.Equal(received.Data.Reference,
            (await service.ReceiveTransferAsync(dispatch.Reference, request)).Data!.Reference);
        Assert.Equal(409, (await service.ReceiveTransferAsync(dispatch.Reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(409, (await service.ReverseMovementAsync(dispatch.Reference,
            new ReverseInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = dispatch.RowVersion,
                Reason = "Cannot cancel received transfer"
            })).StatusCode);
        var reverseReceipt = await service.ReverseMovementAsync(received.Data.Reference,
            new ReverseInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = received.Data.RowVersion,
                Reason = "Correct receiving record"
            });
        Assert.Equal(200, reverseReceipt.StatusCode);
        var reverseDispatch = await service.ReverseMovementAsync(dispatch.Reference,
            new ReverseInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = dispatch.RowVersion,
                Reason = "Cancel the in transit dispatch"
            });
        Assert.Equal(200, reverseDispatch.StatusCode);
        Assert.Equal(10m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        Assert.Equal(0m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        Assert.Equal(409, (await service.ReceiveTransferAsync(dispatch.Reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_receipt_and_cross_tenant_direct_id_cannot_duplicate_stock()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        Guid reference;
        await using (var setup = Open(f.Options, f.TenantId))
            reference = (await DispatchAsync(setup, f)).Reference;

        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var a = Service(first, f).ReceiveTransferAsync(reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() });
        var b = Service(second, f).ReceiveTransferAsync(reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() });
        var results = await Task.WhenAll(a, b);
        Assert.Single(results.Where(x => x.StatusCode == 200));
        Assert.Single(results.Where(x => x.StatusCode == 409));
        await using var verify = Open(f.Options, f.TenantId);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
            x.SourceType == "TransferReceipt" && x.SourceId ==
                verify.InventoryMovements.Where(m => m.PublicId == reference).Select(m => m.Id).First()));
        Assert.Equal(6m, (await Service(verify, f).GetItemBalanceAsync(f.ItemId, f.DestinationId, null))
            .Data!.QuantityOnHand);
        Assert.Equal(403, (await Service(verify, f, "Student").ReceiveTransferAsync(reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999).ReceiveTransferAsync(
            reference, new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
    }
}
