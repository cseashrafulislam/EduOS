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
        string role = "TenantAdmin", long? tenantId = null, TimeProvider? time = null) =>
        new(db, new Actor(tenantId ?? f.TenantId, f.UserId, role),
            time ?? TimeProvider.System, NullLogger<InventoryMovementService>.Instance);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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

        // HttpContextAccessor is AsyncLocal; opening a foreign-tenant fixture changes
        // the ambient test context. Restore the owner context before asserting release.
        await using var owner = Open(f.Options, f.TenantId);
        var ownerService = Service(owner, f);
        var released = await ownerService.ReleaseReservationAsync(draft.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = hold.Data!.RowVersion,
                Reason = "Approved request was withdrawn"
            });
        Assert.Equal(200, released.StatusCode);
        Assert.Empty((await ownerService.GetReservationRegisterAsync(null, null, 1, 25)).Data!.Items);
    }

    [Fact]
    public async Task Partial_reserved_issue_posts_immutable_children_and_preserves_remaining_hold()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 8m);
        var held = await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion });
        Assert.Equal(200, held.StatusCode);

        var request = new FulfillInventoryReservedIssueRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = held.Data!.RowVersion,
            Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 3m }]
        };
        var first = await service.FulfillReservedIssueAsync(draft.Reference, request);
        Assert.Equal(200, first.StatusCode);
        Assert.Equal("Posted", first.Data!.StateCode);
        Assert.Equal("ReservationIssueFulfillment", first.Data.SourceType);
        Assert.Equal(3m, first.Data.Lines.Single().Quantity);
        Assert.Equal(first.Data.Reference,
            (await service.FulfillReservedIssueAsync(draft.Reference, request)).Data!.Reference);
        Assert.Equal(409, (await service.FulfillReservedIssueAsync(draft.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = request.ClientRequestId, RowVersion = request.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 2m }]
            })).StatusCode);

        var remaining = (await service.GetMovementAsync(draft.Reference)).Data!;
        Assert.Equal("Reserved", remaining.StateCode);
        Assert.Equal(8m, remaining.Lines.Single().Quantity);
        Assert.Equal(3m, remaining.Lines.Single().FulfilledQuantity);
        var balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(7m, balance.QuantityOnHand);
        Assert.Equal(5m, balance.ReservedQuantity);
        Assert.Equal(2m, balance.AvailableQuantity);
        var report = (await service.GetReservationRegisterAsync(f.SourceId, null, 1, 25)).Data!;
        Assert.Single(report.Items);
        Assert.Equal(8m, report.Items[0].RequestedQuantity);
        Assert.Equal(3m, report.Items[0].FulfilledQuantity);
        Assert.Equal(5m, report.Items[0].ReservedQuantity);
        Assert.Equal(409, (await service.PostMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = remaining.RowVersion })).StatusCode);

        var competing = await DraftIssueAsync(db, f, 3m);
        Assert.Equal(409, (await service.PostMovementAsync(competing.Reference,
            new PostInventoryMovementRequestDto { RowVersion = competing.RowVersion })).StatusCode);

        var final = await service.FulfillReservedIssueAsync(draft.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = remaining.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
            });
        Assert.Equal(200, final.StatusCode);
        var completed = (await service.GetMovementAsync(draft.Reference)).Data!;
        Assert.Equal("Fulfilled", completed.StateCode);
        Assert.Equal(8m, completed.Lines.Single().FulfilledQuantity);
        Assert.Empty((await service.GetReservationRegisterAsync(f.SourceId, null, 1, 25)).Data!.Items);
        balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(2m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(409, (await service.FulfillReservedIssueAsync(draft.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = completed.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
            })).StatusCode);
        Assert.Equal(200, (await service.ReverseMovementAsync(first.Data.Reference,
            new ReverseInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = first.Data.RowVersion,
                Reason = "Correct the previously posted partial issue"
            })).StatusCode);
        balance = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(5m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
    }

    [Fact]
    public async Task Concurrent_partial_issue_commands_cannot_overfulfil_or_cross_tenant()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        InventoryMovementDto held;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            var draft = await DraftIssueAsync(setup, f, 8m);
            held = (await Service(setup, f).ReserveMovementAsync(draft.Reference,
                new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
        }
        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var results = await Task.WhenAll(
            Service(first, f).FulfillReservedIssueAsync(held.Reference,
                new FulfillInventoryReservedIssueRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
                }),
            Service(second, f).FulfillReservedIssueAsync(held.Reference,
                new FulfillInventoryReservedIssueRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
                }));
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.TenantId);
        var service = Service(verify, f);
        Assert.Equal(5m, (await service.GetMovementAsync(held.Reference)).Data!.Lines.Single().FulfilledQuantity);
        Assert.Equal(5m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        Assert.Equal(3m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.ReservedQuantity);
        Assert.Equal(403, (await Service(verify, f, "Student").FulfillReservedIssueAsync(held.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
            })).StatusCode);
        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999)
            .FulfillReservedIssueAsync(held.Reference,
                new FulfillInventoryReservedIssueRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
                })).StatusCode);
    }


    [Fact]
    public async Task Reserved_transfer_partial_dispatch_receipt_and_retry_keep_stock_consistent()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await service.CreateMovementAsync(new CreateInventoryMovementRequestDto
        {
            ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Transfer",
            SourceType = "ManualTransfer", FromLocationId = f.SourceId,
            ToLocationId = f.DestinationId, Remarks = "Approved partial warehouse transfer",
            Lines = [new CreateInventoryMovementLineRequestDto
            { InventoryItemId = f.ItemId, Quantity = 8m, UnitCost = 5m }]
        });
        Assert.Equal(200, draft.StatusCode);
        var held = (await service.ReserveMovementAsync(draft.Data!.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.Data.RowVersion })).Data!;
        var request = new FulfillInventoryReservedTransferRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
            Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 3m }]
        };
        var dispatch = await service.FulfillReservedTransferAsync(held.Reference, request);
        Assert.Equal(200, dispatch.StatusCode);
        Assert.Equal("TransferDispatch", dispatch.Data!.SourceType);
        Assert.Equal("Issue", dispatch.Data.MovementTypeCode);
        Assert.Equal(f.DestinationId, dispatch.Data.ToLocationId);
        Assert.Equal(dispatch.Data.Reference,
            (await service.FulfillReservedTransferAsync(held.Reference, request)).Data!.Reference);
        Assert.Equal(409, (await service.FulfillReservedTransferAsync(held.Reference,
            new FulfillInventoryReservedTransferRequestDto
            {
                ClientRequestId = request.ClientRequestId, RowVersion = request.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 2m }]
            })).StatusCode);
        var source = (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(7m, source.QuantityOnHand);
        Assert.Equal(5m, source.ReservedQuantity);
        Assert.Equal(2m, source.AvailableQuantity);
        Assert.Equal(0m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        Assert.Equal("InTransit", (await service.GetMovementAsync(dispatch.Data.Reference)).Data!.TransferState);
        var receiptKey = new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() };
        var receipt = await service.ReceiveTransferAsync(dispatch.Data.Reference, receiptKey);
        Assert.Equal(200, receipt.StatusCode);
        Assert.Equal(receipt.Data!.Reference,
            (await service.ReceiveTransferAsync(dispatch.Data.Reference, receiptKey)).Data!.Reference);
        Assert.Equal(409, (await service.ReceiveTransferAsync(dispatch.Data.Reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(3m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        var remaining = (await service.GetMovementAsync(held.Reference)).Data!;
        var second = await service.FulfillReservedTransferAsync(held.Reference,
            new FulfillInventoryReservedTransferRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = remaining.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
            });
        Assert.Equal(200, second.StatusCode);
        Assert.Equal("Fulfilled", (await service.GetMovementAsync(held.Reference)).Data!.StateCode);
        Assert.Equal(2m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        Assert.Equal(0m, (await service.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.ReservedQuantity);
        Assert.Equal(200, (await service.ReceiveTransferAsync(second.Data!.Reference,
            new ReceiveInventoryTransferRequestDto { ClientRequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(8m, (await service.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
    }

    [Fact]
    public async Task Competing_partial_transfer_dispatches_enforce_rowversion_and_tenant_roles()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        InventoryMovementDto held;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            var service = Service(setup, f);
            var draft = await service.CreateMovementAsync(new CreateInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Transfer",
                SourceType = "ManualTransfer", FromLocationId = f.SourceId,
                ToLocationId = f.DestinationId, Remarks = "Concurrent reserved warehouse transfer",
                Lines = [new CreateInventoryMovementLineRequestDto
                { InventoryItemId = f.ItemId, Quantity = 8m, UnitCost = 5m }]
            });
            held = (await service.ReserveMovementAsync(draft.Data!.Reference,
                new PostInventoryMovementRequestDto { RowVersion = draft.Data.RowVersion })).Data!;
        }
        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var results = await Task.WhenAll(
            Service(first, f).FulfillReservedTransferAsync(held.Reference,
                new FulfillInventoryReservedTransferRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
                }),
            Service(second, f).FulfillReservedTransferAsync(held.Reference,
                new FulfillInventoryReservedTransferRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
                }));
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.TenantId);
        Assert.Equal(5m, (await Service(verify, f).GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        Assert.Equal(3m, (await Service(verify, f).GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.ReservedQuantity);
        Assert.Equal(403, (await Service(verify, f, "Student").FulfillReservedTransferAsync(
            held.Reference, new FulfillInventoryReservedTransferRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
            })).StatusCode);
        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999)
            .FulfillReservedTransferAsync(held.Reference,
                new FulfillInventoryReservedTransferRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
                })).StatusCode);
    }

    [Fact]
    public async Task Scheduled_reservation_expiry_releases_only_remaining_hold_and_preserves_evidence()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 8m);
        var held = (await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
        var partial = await service.FulfillReservedIssueAsync(held.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = held.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 3m }]
            });
        Assert.Equal(200, partial.StatusCode);
        var parent = (await service.GetMovementAsync(held.Reference)).Data!;
        var now = DateTimeOffset.UtcNow;
        var deadline = now.AddMinutes(30).UtcDateTime;
        var scheduled = await service.ScheduleReservationExpiryAsync(parent.Reference,
            new ScheduleInventoryReservationExpiryRequestDto
            { RowVersion = parent.RowVersion, ExpiresAtUtc = deadline });
        Assert.Equal(200, scheduled.StatusCode);
        Assert.Equal(deadline, scheduled.Data!.ReservationExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, scheduled.Data.ReservationExpiresAtUtc!.Value.Kind);
        Assert.Equal(200, (await service.ScheduleReservationExpiryAsync(parent.Reference,
            new ScheduleInventoryReservationExpiryRequestDto
            { RowVersion = parent.RowVersion, ExpiresAtUtc = deadline })).StatusCode);
        Assert.Equal(409, (await service.ScheduleReservationExpiryAsync(parent.Reference,
            new ScheduleInventoryReservationExpiryRequestDto
            { RowVersion = scheduled.Data.RowVersion, ExpiresAtUtc = deadline.AddHours(1) })).StatusCode);
        Assert.Equal(403, (await Service(db, f, "Student").ExpireReservationAsync(parent.Reference,
            new ExpireInventoryReservationRequestDto
            { RowVersion = scheduled.Data.RowVersion, Reason = "Student cannot expire warehouse hold" })).StatusCode);
        Assert.Equal(409, (await service.ExpireReservationAsync(parent.Reference,
            new ExpireInventoryReservationRequestDto
            { RowVersion = scheduled.Data.RowVersion, Reason = "Approved warehouse reservation deadline" })).StatusCode);

        await using var dueDb = Open(f.Options, f.TenantId);
        var dueService = Service(dueDb, f, time: new FixedClock(now.AddMinutes(31)));
        Assert.Equal(409, (await dueService.FulfillReservedIssueAsync(parent.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = scheduled.Data.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
            })).StatusCode);
        var expiry = await dueService.ExpireReservationAsync(parent.Reference,
            new ExpireInventoryReservationRequestDto
            { RowVersion = scheduled.Data.RowVersion, Reason = "Approved warehouse reservation deadline" });
        Assert.Equal(200, expiry.StatusCode);
        Assert.Equal("Expired", expiry.Data!.StateCode);
        Assert.Equal(f.UserId, expiry.Data.ReservationExpiredByUserId);
        Assert.Equal(DateTimeKind.Utc, expiry.Data.ReservationExpiredAtUtc!.Value.Kind);
        Assert.Equal(200, (await dueService.ExpireReservationAsync(parent.Reference,
            new ExpireInventoryReservationRequestDto
            { RowVersion = scheduled.Data.RowVersion, Reason = "Approved warehouse reservation deadline" })).StatusCode);
        var balance = (await dueService.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(7m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
        var history = (await dueService.GetReservationRegisterAsync(f.SourceId, null, 1, 25,
            includeClosed: true)).Data!;
        Assert.Contains(history.Items, x => x.MovementReference == parent.Reference &&
            x.StateCode == "Expired" && x.FulfilledQuantity == 3m &&
            x.ReleasedQuantity == 5m && x.ReservedQuantity == 0m &&
            x.ReservationExpiresAtUtc!.Value.Kind == DateTimeKind.Utc &&
            x.ReservationExpiredAtUtc!.Value.Kind == DateTimeKind.Utc);
        Assert.Equal(409, (await dueService.PostMovementAsync(parent.Reference,
            new PostInventoryMovementRequestDto { RowVersion = expiry.Data.RowVersion })).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => dueDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.InventoryMovements SET ReservationExpiryReason = { "Tampered reason" } WHERE PublicId = {parent.Reference}"));
    }


    [Fact]
    public async Task Reservation_hold_origin_and_deadline_actor_survive_partial_fulfilment()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 8m);
        var held = (await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
        Assert.NotNull(held.ReservationHeldAtUtc);
        Assert.Equal(DateTimeKind.Utc, held.ReservationHeldAtUtc!.Value.Kind);
        var before = (await service.GetReservationRegisterAsync(f.SourceId, null, 1, 25)).Data!.Items.Single();
        Assert.False(before.HoldStartIsEstimated);
        Assert.Equal(held.ReservationHeldAtUtc!.Value, before.HeldSinceUtc);
        var deadline = DateTime.UtcNow.AddHours(2);
        var scheduled = (await service.ScheduleReservationExpiryAsync(held.Reference,
            new ScheduleInventoryReservationExpiryRequestDto
            { RowVersion = held.RowVersion, ExpiresAtUtc = deadline })).Data!;
        Assert.Equal(f.UserId, scheduled.ReservationDeadlineScheduledByUserId);
        Assert.NotNull(scheduled.ReservationDeadlineScheduledAtUtc);
        var partial = await service.FulfillReservedIssueAsync(held.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = scheduled.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 3m }]
            });
        Assert.Equal(200, partial.StatusCode);
        var after = (await service.GetReservationRegisterAsync(f.SourceId, null, 1, 25)).Data!.Items.Single();
        Assert.Equal(before.HeldSinceUtc, after.HeldSinceUtc);
        Assert.Equal(scheduled.ReservationDeadlineScheduledAtUtc, after.ReservationDeadlineScheduledAtUtc);
        Assert.Equal(f.UserId, after.ReservationDeadlineScheduledByUserId);
        Assert.Equal(5m, after.ReservedQuantity);
        Assert.Equal(0, (await service.GetReservationRegisterAsync(f.SourceId, null, 1, 25,
            overdueOnly: true)).Data!.TotalCount);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.InventoryMovements SET ReservationHeldAtUtc = {DateTime.UtcNow.AddDays(-2)} WHERE PublicId = {held.Reference}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.InventoryMovements SET ReservationDeadlineScheduledByUserId = {f.UserId + 1} WHERE PublicId = {held.Reference}"));
    }

    [Fact]
    public async Task Concurrent_due_expiry_and_release_preserve_stock_once()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        InventoryMovementDto scheduled;
        var now = DateTimeOffset.UtcNow;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            var service = Service(setup, f);
            var draft = await DraftIssueAsync(setup, f, 8m);
            var held = (await service.ReserveMovementAsync(draft.Reference,
                new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
            scheduled = (await service.ScheduleReservationExpiryAsync(held.Reference,
                new ScheduleInventoryReservationExpiryRequestDto
                { RowVersion = held.RowVersion, ExpiresAtUtc = now.AddMinutes(1).UtcDateTime })).Data!;
        }
        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var result = await Task.WhenAll(
            Service(first, f, time: new FixedClock(now.AddMinutes(2))).ExpireReservationAsync(
                scheduled.Reference, new ExpireInventoryReservationRequestDto
                { RowVersion = scheduled.RowVersion, Reason = "Approved expired inventory reservation" }),
            Service(second, f).ReleaseReservationAsync(scheduled.Reference,
                new ReleaseInventoryReservationRequestDto
                { RowVersion = scheduled.RowVersion, Reason = "Approved cancellation of the same stock hold" }));
        Assert.Single(result, x => x.StatusCode == 200);
        Assert.Single(result, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.TenantId);
        var balance = (await Service(verify, f).GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(10m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(10m, balance.AvailableQuantity);
        var history = (await Service(verify, f).GetReservationRegisterAsync(f.SourceId, null, 1, 25,
            includeClosed: true)).Data!.Items.Single();
        Assert.Equal(8m, history.ReleasedQuantity);
        Assert.Contains(history.StateCode, new[] { "Expired", "Cancelled" });
    }


    [Fact]
    public async Task Campus_scoped_reservation_reads_and_direct_id_writes_deny_unassigned_warehouse()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var allowedCampus = new Campus { TenantId = f.TenantId, Code = "INV-A-" + suffix, Name = "Allowed campus" };
        var otherCampus = new Campus { TenantId = f.TenantId, Code = "INV-B-" + suffix, Name = "Other campus" };
        db.Campuses.AddRange(allowedCampus, otherCampus);
        await db.SaveChangesAsync();
        var origin = await db.InventoryLocations.SingleAsync(x => x.Id == f.SourceId);
        var destination = await db.InventoryLocations.SingleAsync(x => x.Id == f.DestinationId);
        origin.CampusId = allowedCampus.Id;
        destination.CampusId = otherCampus.Id;
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = f.TenantId, UserId = f.UserId, CampusId = allowedCampus.Id, IsActive = true
        });
        await db.SaveChangesAsync();

        var admin = Service(db, f);
        var issue = await DraftIssueAsync(db, f, 4m);
        var reservedIssue = await admin.ReserveMovementAsync(issue.Reference,
            new PostInventoryMovementRequestDto { RowVersion = issue.RowVersion });
        Assert.Equal(200, reservedIssue.StatusCode);
        var transfer = await admin.CreateMovementAsync(new CreateInventoryMovementRequestDto
        {
            ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Transfer",
            SourceType = "ManualTransfer", FromLocationId = f.SourceId,
            ToLocationId = f.DestinationId, Remarks = "Campus restricted transfer",
            Lines = [new CreateInventoryMovementLineRequestDto
            { InventoryItemId = f.ItemId, Quantity = 2m, UnitCost = 5m }]
        });
        Assert.Equal(200, transfer.StatusCode);
        var reservedTransfer = await admin.ReserveMovementAsync(transfer.Data!.Reference,
            new PostInventoryMovementRequestDto { RowVersion = transfer.Data.RowVersion });
        Assert.Equal(200, reservedTransfer.StatusCode);

        var restricted = Service(db, f, "InventoryManager");
        var visible = await restricted.GetReservationRegisterAsync(null, null, 1, 25);
        Assert.Equal(200, visible.StatusCode);
        Assert.Single(visible.Data!.Items);
        Assert.Equal(issue.Reference, visible.Data.Items[0].MovementReference);
        Assert.Equal(404, (await restricted.GetReservationRegisterAsync(f.DestinationId, null, 1, 25)).StatusCode);
        Assert.Equal(404, (await restricted.GetMovementAsync(transfer.Data.Reference)).StatusCode);
        Assert.Equal(404, (await restricted.ScheduleReservationExpiryAsync(reservedTransfer.Data!.Reference,
            new ScheduleInventoryReservationExpiryRequestDto
            {
                RowVersion = reservedTransfer.Data.RowVersion,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
            })).StatusCode);
        Assert.Equal(404, (await restricted.ExpireReservationAsync(reservedTransfer.Data.Reference,
            new ExpireInventoryReservationRequestDto
            {
                RowVersion = reservedTransfer.Data.RowVersion,
                Reason = "Unassigned campus cannot expire this reservation"
            })).StatusCode);
        Assert.Equal(404, (await restricted.FulfillReservedTransferAsync(reservedTransfer.Data.Reference,
            new FulfillInventoryReservedTransferRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = reservedTransfer.Data.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 1m }]
            })).StatusCode);
        Assert.Equal(6m, (await admin.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.ReservedQuantity);
    }

    [Fact]
    public async Task Concurrent_partial_issue_and_expiration_preserve_one_authoritative_hold()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var now = DateTimeOffset.UtcNow;
        InventoryMovementDto scheduled;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            var service = Service(setup, f);
            var draft = await DraftIssueAsync(setup, f, 8m);
            var held = (await service.ReserveMovementAsync(draft.Reference,
                new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
            scheduled = (await service.ScheduleReservationExpiryAsync(held.Reference,
                new ScheduleInventoryReservationExpiryRequestDto
                {
                    RowVersion = held.RowVersion, ExpiresAtUtc = now.AddMinutes(1).UtcDateTime
                })).Data!;
        }

        await using var expireDb = Open(f.Options, f.TenantId);
        await using var partialDb = Open(f.Options, f.TenantId);
        var outcomes = await Task.WhenAll(
            Service(expireDb, f, time: new FixedClock(now.AddMinutes(2))).ExpireReservationAsync(
                scheduled.Reference, new ExpireInventoryReservationRequestDto
                {
                    RowVersion = scheduled.RowVersion, Reason = "Approved expiry after hold deadline"
                }),
            Service(partialDb, f, time: new FixedClock(now)).FulfillReservedIssueAsync(
                scheduled.Reference, new FulfillInventoryReservedIssueRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = scheduled.RowVersion,
                    Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 5m }]
                }));
        Assert.Single(outcomes, x => x.StatusCode == 200);
        Assert.Single(outcomes, x => x.StatusCode == 409);

        await using var verify = Open(f.Options, f.TenantId);
        var serviceAfter = Service(verify, f, time: new FixedClock(now.AddMinutes(2)));
        var parent = (await serviceAfter.GetMovementAsync(scheduled.Reference)).Data!;
        if (parent.StateCode == "Reserved")
        {
            Assert.Equal(5m, parent.Lines.Single().FulfilledQuantity);
            Assert.Equal(200, (await serviceAfter.ExpireReservationAsync(parent.Reference,
                new ExpireInventoryReservationRequestDto
                {
                    RowVersion = parent.RowVersion, Reason = "Approved expiry after partial issue"
                })).StatusCode);
        }
        var final = (await serviceAfter.GetMovementAsync(scheduled.Reference)).Data!;
        Assert.Equal("Expired", final.StateCode);
        var balance = (await serviceAfter.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(10m - final.Lines.Single().FulfilledQuantity, balance.QuantityOnHand);
        Assert.Equal(balance.QuantityOnHand, balance.AvailableQuantity);
        var originalId = await verify.InventoryMovements.Where(x => x.PublicId == scheduled.Reference)
            .Select(x => x.Id).SingleAsync();
        Assert.Equal(final.Lines.Single().FulfilledQuantity > 0m ? 1 : 0,
            await verify.InventoryMovements.CountAsync(x => x.SourceType == "ReservationIssueFulfillment" &&
                x.SourceId == originalId && x.StateCode == "Posted"));
        var history = (await serviceAfter.GetReservationRegisterAsync(f.SourceId, null, 1, 25,
            includeClosed: true)).Data!.Items.Single();
        Assert.Equal(8m - final.Lines.Single().FulfilledQuantity, history.ReleasedQuantity);
    }

    [Fact]
    public async Task Due_reservation_posting_race_fails_closed_and_expiration_is_retry_safe()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var now = DateTimeOffset.UtcNow;
        InventoryMovementDto scheduled;
        await using (var setup = Open(f.Options, f.TenantId))
        {
            var service = Service(setup, f);
            var draft = await DraftIssueAsync(setup, f, 7m);
            var held = (await service.ReserveMovementAsync(draft.Reference,
                new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion })).Data!;
            scheduled = (await service.ScheduleReservationExpiryAsync(held.Reference,
                new ScheduleInventoryReservationExpiryRequestDto
                {
                    RowVersion = held.RowVersion, ExpiresAtUtc = now.AddMinutes(1).UtcDateTime
                })).Data!;
        }
        await using var first = Open(f.Options, f.TenantId);
        await using var second = Open(f.Options, f.TenantId);
        var result = await Task.WhenAll(
            Service(first, f, time: new FixedClock(now.AddMinutes(2))).ExpireReservationAsync(
                scheduled.Reference, new ExpireInventoryReservationRequestDto
                {
                    RowVersion = scheduled.RowVersion, Reason = "Approved due reservation expiry"
                }),
            Service(second, f, time: new FixedClock(now.AddMinutes(2))).PostMovementAsync(
                scheduled.Reference, new PostInventoryMovementRequestDto { RowVersion = scheduled.RowVersion }));
        Assert.Single(result, x => x.StatusCode == 200);
        Assert.Single(result, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.TenantId);
        var serviceAfter = Service(verify, f, time: new FixedClock(now.AddMinutes(2)));
        Assert.Equal("Expired", (await serviceAfter.GetMovementAsync(scheduled.Reference)).Data!.StateCode);
        var balance = (await serviceAfter.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        Assert.Equal(10m, balance.QuantityOnHand);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(10m, balance.AvailableQuantity);
    }


    [Fact]
    public async Task Reservation_coverage_reconciles_current_lot_stock_after_partial_issue_and_release()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var draft = await DraftIssueAsync(db, f, 8m);
        var held = await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion });
        Assert.Equal(200, held.StatusCode);

        var first = await service.GetReservationCoverageAsync(f.SourceId, "PEN-", false, 1, 25);
        Assert.Equal(200, first.StatusCode);
        var row = Assert.Single(first.Data!.Items);
        Assert.Equal(10m, row.QuantityOnHand);
        Assert.Equal(8m, row.ReservedQuantity);
        Assert.Equal(2m, row.AvailableQuantity);
        Assert.Equal(0m, row.ShortfallQuantity);
        Assert.Equal("Covered", row.StatusCode);
        Assert.Equal(1, row.ActiveHoldLines);
        Assert.Equal(f.ItemId, row.InventoryItemId);
        Assert.Equal(0, (await service.GetReservationCoverageAsync(f.DestinationId,
            null, false, 1, 25)).Data!.TotalCount);
        Assert.Equal(404, (await service.GetReservationCoverageAsync(long.MaxValue,
            null, false, 1, 25)).StatusCode);
        Assert.Equal(403, (await Service(db, f, "Student").GetReservationCoverageAsync(
            f.SourceId, null, false, 1, 25)).StatusCode);

        var part = await service.FulfillReservedIssueAsync(draft.Reference,
            new FulfillInventoryReservedIssueRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = held.Data!.RowVersion,
                Lines = [new FulfillInventoryReservedIssueLineDto { LineNo = 1, Quantity = 3m }]
            });
        Assert.Equal(200, part.StatusCode);
        row = Assert.Single((await service.GetReservationCoverageAsync(
            f.SourceId, null, false, 1, 25)).Data!.Items);
        Assert.Equal(7m, row.QuantityOnHand);
        Assert.Equal(5m, row.ReservedQuantity);
        Assert.Equal(2m, row.AvailableQuantity);

        var remaining = (await service.GetMovementAsync(draft.Reference)).Data!;
        var released = await service.ReleaseReservationAsync(draft.Reference,
            new ReleaseInventoryReservationRequestDto
            {
                RowVersion = remaining.RowVersion,
                Reason = "Manager cancelled the outstanding partial issue"
            });
        Assert.Equal(200, released.StatusCode);
        Assert.Empty((await service.GetReservationCoverageAsync(
            f.SourceId, null, false, 1, 25)).Data!.Items);

        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999)
            .GetReservationCoverageAsync(f.SourceId, null, false, 1, 25)).StatusCode);
    }


    [Fact]
    public async Task Lot_aging_reconciles_canonical_stock_reservations_and_denies_foreign_scope()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.TenantId);
        var service = Service(db, f);
        var initial = await service.GetLotAgingAsync(f.SourceId, "PEN-", 0, 1, 25);
        Assert.Equal(200, initial.StatusCode);
        var lot = Assert.Single(initial.Data!.Items);
        Assert.Equal(10m, lot.QuantityOnHand);
        Assert.Equal(50m, lot.RecordedCostBalance);
        Assert.Equal(0m, lot.ReservedQuantity);
        Assert.NotNull(lot.LatestInboundUtc);
        Assert.Equal("Current", lot.StatusCode);
        Assert.Equal(0, (await service.GetLotAgingAsync(f.SourceId, null, 90, 1, 25))
            .Data!.TotalCount);

        var draft = await DraftIssueAsync(db, f, 6m);
        var reserved = await service.ReserveMovementAsync(draft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = draft.RowVersion });
        Assert.Equal(200, reserved.StatusCode);
        lot = Assert.Single((await service.GetLotAgingAsync(f.SourceId, null, 0, 1, 25)).Data!.Items);
        Assert.Equal(10m, lot.QuantityOnHand);
        Assert.Equal(6m, lot.ReservedQuantity);
        Assert.Equal(4m, lot.AvailableQuantity);
        Assert.Equal(403, (await Service(db, f, "Student")
            .GetLotAgingAsync(f.SourceId, null, 0, 1, 25)).StatusCode);
        Assert.Equal(404, (await service.GetLotAgingAsync(long.MaxValue, null, 0, 1, 25)).StatusCode);
        Assert.Equal(400, (await service.GetLotAgingAsync(f.SourceId, null, -1, 1, 25)).StatusCode);

        await using var foreign = Open(f.Options, f.TenantId + 999);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 999)
            .GetLotAgingAsync(f.SourceId, null, 0, 1, 25)).StatusCode);
    }
}
