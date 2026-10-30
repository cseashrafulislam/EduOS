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
        var http = new DefaultHttpContext();
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
        Assert.Equal(409, (await service.ReceiveTransferAsync(dispatch.Reference, request)).StatusCode);
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
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
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

    private static async Task<InventoryMovementDto> DraftIssueAsync(EduOSDbContext db, Fixture f, decimal quantity)
    {
        var draft = await Service(db, f).CreateMovementAsync(new CreateInventoryMovementRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            MovementTypeCode = "Issue",
            SourceType = "ManualIssue",
            FromLocationId = f.SourceId,
            Remarks = "Authorized issue for inventory reservation",
            Lines = [new CreateInventoryMovementLineRequestDto
            {
                InventoryItemId = f.ItemId, Quantity = quantity, UnitCost = 0m
            }]
        });
        Assert.Equal(200, draft.StatusCode);
        return draft.Data!;
    }

    [Fact]
    public async Task Reserved_stock_blocks_unrelated_post_and_release_restores_available_quantity()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var first = await DraftIssueAsync(db, f, 8m);
        var reserved = await service.ReserveMovementAsync(first.Reference,
            new PostInventoryMovementRequestDto { RowVersion = first.RowVersion });
        Assert.Equal(200, reserved.StatusCode);
        var balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(10m, balance.QuantityOnHand);
        Assert.Equal(8m, balance.ReservedQuantity);
        Assert.Equal(2m, balance.AvailableQuantity);

        var second = await DraftIssueAsync(db, f, 3m);
        Assert.Equal(409, (await service.PostMovementAsync(second.Reference,
            new PostInventoryMovementRequestDto { RowVersion = second.RowVersion })).StatusCode);
        var release = await service.ReleaseReservationAsync(first.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = reserved.Data!.RowVersion,
                Reason = "Approved issue request was cancelled"
            });
        Assert.Equal(200, release.StatusCode);
        Assert.Equal("Cancelled", release.Data!.StateCode);
        Assert.Equal(200, (await service.ReleaseReservationAsync(first.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = reserved.Data.RowVersion,
                Reason = "Approved issue request was cancelled"
            })).StatusCode);
        Assert.Equal(200, (await service.PostMovementAsync(second.Reference,
            new PostInventoryMovementRequestDto { RowVersion = second.RowVersion })).StatusCode);
        Assert.Equal(7m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null))
            .Data!.AvailableQuantity);
        Assert.Equal(409, (await service.ReserveMovementAsync(first.Reference,
            new PostInventoryMovementRequestDto { RowVersion = first.RowVersion })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_oversell_and_receipt_reversal_respects_holds()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        InventoryMovementDto a, b;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            a = await DraftIssueAsync(setup, f, 7m);
            b = await DraftIssueAsync(setup, f, 7m);
        }
        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var results = await Task.WhenAll(
            Service(first, f).ReserveMovementAsync(a.Reference,
                new PostInventoryMovementRequestDto { RowVersion = a.RowVersion }),
            Service(second, f).ReserveMovementAsync(b.Reference,
                new PostInventoryMovementRequestDto { RowVersion = b.RowVersion }));
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);

        await using var verify = Open(f.Options, f.TenantId);
        var service = Service(verify, f);
        var balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(10m, balance.QuantityOnHand);
        Assert.Equal(7m, balance.ReservedQuantity);
        Assert.Equal(3m, balance.AvailableQuantity);
        var opening = await verify.InventoryMovements.AsNoTracking()
            .SingleAsync(x => x.TenantId == f.TenantId && x.SourceType == "OpeningBalance");
        Assert.Equal(409, (await service.ReverseMovementAsync(opening.PublicId,
            new ReverseInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                RowVersion = Convert.ToBase64String(opening.RowVersion),
                Reason = "Attempt to reverse reserved opening stock"
            })).StatusCode);
        Assert.Equal(403, (await Service(verify, f, "Student").ReserveMovementAsync(
            a.Reference, new PostInventoryMovementRequestDto { RowVersion = a.RowVersion })).StatusCode);
        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999)
            .ReserveMovementAsync(a.Reference,
                new PostInventoryMovementRequestDto { RowVersion = a.RowVersion })).StatusCode);
    }

    [Fact]
    public async Task Posting_reserved_issue_consumes_own_hold_without_duplicate_stock_debit()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 6m);
        var reserved = await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion });
        Assert.Equal(200, reserved.StatusCode);
        Assert.Equal(200, (await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).StatusCode);
        var posted = await service.PostMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = reserved.Data!.RowVersion });
        Assert.Equal(200, posted.StatusCode);
        Assert.Equal("Posted", posted.Data!.StateCode);
        var balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(4m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(4m, balance.AvailableQuantity);
        Assert.Equal(409, (await service.ReleaseReservationAsync(draft.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = reserved.Data.RowVersion,
                Reason = "Cannot release already posted issue"
            })).StatusCode);
    }


    [Fact]
    public async Task Reservation_register_is_line_scoped_paged_and_revocation_fresh()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 4m);
        var hold = await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion });
        Assert.Equal(200, hold.StatusCode);

        var report = await service.GetReservationRegisterAsync(f.SourceId, "PEN-", 1, 25);
        Assert.Equal(200, report.StatusCode);
        Assert.Equal(1, report.Data!.TotalCount);
        Assert.Single(report.Data.Items);
        Assert.Equal(4m, report.Data.Items[0].ReservedQuantity);
        Assert.Equal(f.ItemId, report.Data.Items[0].InventoryItemId);
        Assert.Equal(draft.Reference, report.Data.Items[0].MovementReference);
        Assert.Equal(0, (await service.GetReservationRegisterAsync(f.DestinationId, null, 1, 25)).Data!.TotalCount);
        Assert.Equal(404, (await service.GetReservationRegisterAsync(long.MaxValue, null, 1, 25)).StatusCode);
        Assert.Equal(403, (await Service(db, f, "Student").GetReservationRegisterAsync(null, null, 1, 25)).StatusCode);

        await using (var foreign = Open(f.Options, f.TenantId + 999))
            Assert.Empty((await Service(foreign, f, tenantId: f.TenantId + 999)
                .GetReservationRegisterAsync(null, null, 1, 25)).Data!.Items);

        var released = await service.ReleaseReservationAsync(draft.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = hold.Data!.RowVersion,
                Reason = "Approved request was withdrawn"
            });
        Assert.Equal(200, released.StatusCode);
        Assert.Empty((await service.GetReservationRegisterAsync(null, null, 1, 25)).Data!.Items);
    }
}
