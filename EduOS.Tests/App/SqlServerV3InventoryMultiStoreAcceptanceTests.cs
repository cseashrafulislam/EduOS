using EduOS.App.Controllers;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3InventoryTransferLifecycleTests
{
    [Fact]
    public async Task Print_count_uses_fresh_authorized_blind_draft_projection_not_private_book_fields()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var makerDb = OpenAs(f.Options, f.TenantId, f.UserId);
        var service = Service(makerDb, f);
        var draft = await service.StartPhysicalCountAsync(CountRequest(f));
        Assert.Equal(200, draft.StatusCode);
        var print = new InventoryMovementsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = await print.PrintPhysicalCount(draft.Data!.Reference);
        var model = Assert.IsType<InventoryPhysicalCountDto>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Blind);
        Assert.Equal("Draft", model.StateCode);
        var line = Assert.Single(model.Lines);
        Assert.Null(line.BookQuantity);
        Assert.Null(line.BookValue);
        Assert.Null(line.ReservedQuantity);
        Assert.Null(line.SuggestedVariance);
        Assert.Equal("private, no-store", print.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(400, Assert.IsType<BadRequestResult>(await print.PrintPhysicalCount(Guid.Empty)).StatusCode);
    }

    private static async Task<long> ThirdStoreAsync(Fixture fixture)
    {
        await using var db = Open(fixture.Options, fixture.TenantId);
        var store = new InventoryLocation
        {
            TenantId = fixture.TenantId, Code = "THIRD-" + Guid.NewGuid().ToString("N")[..12],
            Name = "Independent third store", IsActive = true
        };
        db.InventoryLocations.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }

    private static async Task<InventoryMovementDto> DraftStoreTransferAsync(
        EduOS.Persistence.Context.EduOSDbContext db, Fixture fixture, long origin, long destination,
        decimal amount, string lot = "")
    {
        var result = await Service(db, fixture).CreateMovementAsync(new CreateInventoryMovementRequestDto
        {
            ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Transfer", SourceType = "ManualTransfer",
            FromLocationId = origin, ToLocationId = destination,
            Remarks = "Verified cross-store stock movement with lot provenance",
            Lines = [new CreateInventoryMovementLineRequestDto
            {
                InventoryItemId = fixture.ItemId, LotOrBatchNo = lot, Quantity = amount, UnitCost = 0m
            }]
        });
        Assert.True(result.Success, result.Message);
        return result.Data!;
    }

    [Fact]
    public async Task Three_stores_reconcile_posted_stock_reservation_and_independent_count_without_cross_store_mixing()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var thirdId = await ThirdStoreAsync(f);
        var checkerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto submitted;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
        {
            var maker = Service(makerDb, f);
            var issue = await DraftIssueAsync(makerDb, f, 4m);
            Assert.Equal(200, (await maker.ReserveMovementAsync(issue.Reference,
                new PostInventoryMovementRequestDto { RowVersion = issue.RowVersion })).StatusCode);
            var blocked = await DraftStoreTransferAsync(makerDb, f, f.SourceId, thirdId, 7m);
            Assert.Equal(409, (await maker.PostMovementAsync(blocked.Reference,
                new PostInventoryMovementRequestDto { RowVersion = blocked.RowVersion })).StatusCode);
            var first = await DraftStoreTransferAsync(makerDb, f, f.SourceId, f.DestinationId, 6m);
            Assert.Equal(200, (await maker.PostMovementAsync(first.Reference,
                new PostInventoryMovementRequestDto { RowVersion = first.RowVersion })).StatusCode);
            var second = await DraftStoreTransferAsync(makerDb, f, f.DestinationId, thirdId, 2m);
            Assert.Equal(200, (await maker.PostMovementAsync(second.Reference,
                new PostInventoryMovementRequestDto { RowVersion = second.RowVersion })).StatusCode);

            var originBalance = (await maker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
            var destinationBalance = (await maker.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!;
            var thirdBalance = (await maker.GetItemBalanceAsync(f.ItemId, thirdId, null)).Data!;
            Assert.Equal(4m, originBalance.QuantityOnHand);
            Assert.Equal(4m, originBalance.ReservedQuantity);
            Assert.Equal(0m, originBalance.AvailableQuantity);
            Assert.Equal(4m, destinationBalance.QuantityOnHand);
            Assert.Equal(2m, thirdBalance.QuantityOnHand);
            Assert.Equal(10m, originBalance.QuantityOnHand + destinationBalance.QuantityOnHand + thirdBalance.QuantityOnHand);

            submitted = await OpenAndSubmitCountAsync(makerDb, f, 3m, f.DestinationId);
            Assert.Equal(4m, Assert.Single(submitted.Lines).BookQuantity);
            Assert.Equal(-1m, Assert.Single(submitted.Lines).SuggestedVariance);
            // The print endpoint delegates to the same tenant-filtered count service;
            // no global stock or records from another store may be served.
            var print = new InventoryMovementsController(maker)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            var printed = await print.PrintPhysicalCount(submitted.Reference);
            var view = Assert.IsType<ViewResult>(printed);
            var sheet = Assert.IsType<InventoryPhysicalCountDto>(view.Model);
            Assert.Equal(f.DestinationId, sheet.LocationId);
            Assert.Equal(4m, Assert.Single(sheet.Lines).BookQuantity);
            Assert.Equal("private, no-store", print.Response.Headers["Cache-Control"].ToString());
        }
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, checkerId))
        {
            var checker = CountChecker(checkerDb, f, checkerId);
            var approval = new ReviewInventoryPhysicalCountRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
                Decision = "Approve", VarianceMode = "Automatic",
                Reason = "Independent second store inspection reconciled one damaged unit"
            };
            var accepted = await checker.ReviewPhysicalCountAsync(submitted.Reference, approval);
            Assert.Equal(200, accepted.StatusCode);
            Assert.Equal(200, (await checker.ReviewPhysicalCountAsync(submitted.Reference, approval)).StatusCode);
            var origin = (await checker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
            var destination = (await checker.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!;
            var third = (await checker.GetItemBalanceAsync(f.ItemId, thirdId, null)).Data!;
            Assert.Equal(4m, origin.QuantityOnHand);
            Assert.Equal(4m, origin.ReservedQuantity);
            Assert.Equal(3m, destination.QuantityOnHand);
            Assert.Equal(2m, third.QuantityOnHand);
            Assert.Equal(9m, origin.QuantityOnHand + destination.QuantityOnHand + third.QuantityOnHand);
            Assert.Equal(1, await checkerDb.InventoryMovements.CountAsync(x => x.SourceType == "PhysicalStockCount"));
        }
        await using (var foreignDb = OpenAs(f.Options, f.TenantId + 90000, f.UserId))
        {
            var foreign = new InventoryMovementsController(Service(foreignDb, f, tenantId: f.TenantId + 90000))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            var denied = await foreign.PrintPhysicalCount(submitted.Reference);
            Assert.Equal(404, Assert.IsType<StatusCodeResult>(denied).StatusCode);
        }
    }

    [Fact]
    public async Task Competing_seven_unit_transfers_to_different_stores_cannot_overspend_ten_unit_origin()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var thirdId = await ThirdStoreAsync(f);
        InventoryMovementDto firstDraft, secondDraft;
        await using (var setup = OpenAs(f.Options, f.TenantId, f.UserId))
        {
            firstDraft = await DraftStoreTransferAsync(setup, f, f.SourceId, f.DestinationId, 7m);
            secondDraft = await DraftStoreTransferAsync(setup, f, f.SourceId, thirdId, 7m);
        }
        await using var firstDb = OpenAs(f.Options, f.TenantId, f.UserId);
        await using var secondDb = OpenAs(f.Options, f.TenantId, f.UserId);
        var first = Service(firstDb, f).PostMovementAsync(firstDraft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = firstDraft.RowVersion });
        var second = Service(secondDb, f).PostMovementAsync(secondDraft.Reference,
            new PostInventoryMovementRequestDto { RowVersion = secondDraft.RowVersion });
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, x => x.StatusCode == 200);
        Assert.Single(outcomes, x => x.StatusCode == 409);
        await using var verify = OpenAs(f.Options, f.TenantId, f.UserId);
        var checker = Service(verify, f);
        var origin = (await checker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!;
        var destination = (await checker.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!;
        var third = (await checker.GetItemBalanceAsync(f.ItemId, thirdId, null)).Data!;
        Assert.Equal(3m, origin.QuantityOnHand);
        Assert.Equal(7m, destination.QuantityOnHand + third.QuantityOnHand);
        Assert.Equal(10m, origin.QuantityOnHand + destination.QuantityOnHand + third.QuantityOnHand);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
            x.SourceType == "ManualTransfer" && x.StateCode == "Posted"));
    }
}
