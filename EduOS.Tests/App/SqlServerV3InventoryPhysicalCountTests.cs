using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Service.Services.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerV3InventoryTransferLifecycleTests
{
    private static async Task<long> NewCountCheckerAsync(Fixture fixture)
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var db = new EduOS.Persistence.Context.EduOSDbContext(fixture.Options);
        var actor = new ApplicationUser
        {
            UserName = "count-checker-" + suffix,
            NormalizedUserName = ("count-checker-" + suffix).ToUpperInvariant(),
            Email = "count-checker-" + suffix + "@example.test",
            NormalizedEmail = ("count-checker-" + suffix + "@example.test").ToUpperInvariant(),
            FullName = "Independent physical stock counter reviewer"
        };
        db.ApplicationUsers.Add(actor);
        await db.SaveChangesAsync();
        return actor.Id;
    }

    private static InventoryMovementService CountChecker(EduOS.Persistence.Context.EduOSDbContext db, Fixture fixture,
        long checkerId, string role = "TenantAdmin") =>
        new(db, new Actor(fixture.TenantId, checkerId, role), TimeProvider.System,
            NullLogger<InventoryMovementService>.Instance);

    private static StartInventoryPhysicalCountRequestDto CountRequest(Fixture fixture, long? locationId = null) => new()
    {
        ClientRequestId = Guid.NewGuid(), LocationId = locationId ?? fixture.SourceId,
        Blind = true, Reason = "Physical shelf verification and signed reconciliation",
        Lines = [new InventoryPhysicalCountSelectionDto { InventoryItemId = fixture.ItemId, LotOrBatchNo = "" }]
    };

    private static async Task<InventoryPhysicalCountDto> OpenAndSubmitCountAsync(
        EduOS.Persistence.Context.EduOSDbContext db, Fixture fixture, decimal measured, long? locationId = null)
    {
        var service = Service(db, fixture);
        var start = await service.StartPhysicalCountAsync(CountRequest(fixture, locationId));
        Assert.Equal(200, start.StatusCode);
        Assert.Null(Assert.Single(start.Data!.Lines).BookQuantity);
        Assert.Equal("Draft", start.Data.StateCode);
        var submitted = await service.SubmitPhysicalCountAsync(start.Data.Reference,
            new SubmitInventoryPhysicalCountRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = start.Data.RowVersion,
                Lines = [new InventoryPhysicalCountMeasurementDto { LineNo = 1, CountedQuantity = measured }]
            });
        Assert.Equal(200, submitted.StatusCode);
        Assert.Equal("Submitted", submitted.Data!.StateCode);
        return submitted.Data;
    }

    [Fact]
    public async Task Blind_zero_count_requires_independent_approval_and_posts_exactly_once()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var reviewerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto submitted;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
        {
            submitted = await OpenAndSubmitCountAsync(makerDb, f, 0m);
            Assert.Equal(10m, Assert.Single(submitted.Lines).BookQuantity);
            Assert.Equal(-10m, Assert.Single(submitted.Lines).SuggestedVariance);
            Assert.Equal(403, (await Service(makerDb, f).ReviewPhysicalCountAsync(submitted.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
                    Decision = "Approve", VarianceMode = "Automatic", Reason = "Maker cannot approve own stock count"
                })).StatusCode);
        }
        var review = new ReviewInventoryPhysicalCountRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
            Decision = "Approve", VarianceMode = "Automatic", Reason = "Independent shelf inspection confirms stock loss"
        };
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var checker = CountChecker(checkerDb, f, reviewerId);
            var accepted = await checker.ReviewPhysicalCountAsync(submitted.Reference, review);
            Assert.Equal(200, accepted.StatusCode);
            Assert.Equal("Posted", accepted.Data!.StateCode);
            Assert.Equal(-10m, Assert.Single(accepted.Data.Lines).PostedVariance);
            Assert.NotNull(Assert.Single(accepted.Data.Lines).PostedMovementReference);
            Assert.Equal(0m, (await checker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
            Assert.Equal(200, (await checker.ReviewPhysicalCountAsync(submitted.Reference, review)).StatusCode);
            Assert.Equal(409, (await checker.ReviewPhysicalCountAsync(submitted.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
                    Decision = "Approve", VarianceMode = "Automatic", Reason = review.Reason
                })).StatusCode);
            Assert.Equal(1, await checkerDb.InventoryMovements.CountAsync(x =>
                x.SourceType == "PhysicalStockCount"));
            var report = await checker.GetPhysicalCountsAsync(f.SourceId, "Posted", 1, 25);
            Assert.Equal(200, report.StatusCode);
            Assert.Contains(report.Data!.Items, x => x.Reference == submitted.Reference);
        }
    }

    [Fact]
    public async Task Rejected_count_never_posts_and_stale_snapshot_fails_review()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var reviewerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto stale, rejected;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
        {
            stale = await OpenAndSubmitCountAsync(makerDb, f, 9m);
            rejected = await OpenAndSubmitCountAsync(makerDb, f, 9m);
            var service = Service(makerDb, f);
            var adjusted = await service.CreateMovementAsync(new CreateInventoryMovementRequestDto
            {
                ClientRequestId = Guid.NewGuid(), MovementTypeCode = "Receipt", SourceType = "ManualReceipt",
                ToLocationId = f.SourceId, Remarks = "Stock arrived after count snapshot",
                Lines = [new CreateInventoryMovementLineRequestDto { InventoryItemId = f.ItemId, Quantity = 1m, UnitCost = 5m }]
            });
            Assert.Equal(200, adjusted.StatusCode);
            Assert.Equal(200, (await service.PostMovementAsync(adjusted.Data!.Reference,
                new PostInventoryMovementRequestDto { RowVersion = adjusted.Data.RowVersion })).StatusCode);
        }
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var checker = CountChecker(checkerDb, f, reviewerId);
            Assert.Equal(409, (await checker.ReviewPhysicalCountAsync(stale.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = stale.RowVersion,
                    Reason = "Stock changed after initial physical inventory snapshot"
                })).StatusCode);
            Assert.Equal(200, (await checker.ReviewPhysicalCountAsync(rejected.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = rejected.RowVersion, Decision = "Reject",
                    Reason = "Rejected because stock receipt invalidates original count"
                })).StatusCode);
            Assert.Equal("Submitted", (await checker.GetPhysicalCountAsync(stale.Reference)).Data!.StateCode);
            Assert.Equal("Rejected", (await checker.GetPhysicalCountAsync(rejected.Reference)).Data!.StateCode);
            Assert.Equal(0, await checkerDb.InventoryMovements.CountAsync(x => x.SourceType == "PhysicalStockCount"));
            Assert.Equal(11m, (await checker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        }
    }

    [Fact]
    public async Task Manual_final_and_auto_plus_manual_preserve_review_reasons_and_valuations()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var reviewerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto first;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
            first = await OpenAndSubmitCountAsync(makerDb, f, 9m);
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var review = await CountChecker(checkerDb, f, reviewerId).ReviewPhysicalCountAsync(first.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = first.RowVersion,
                    Decision = "Approve", VarianceMode = "ManualFinal", Reason = "Checker verified a larger shortage",
                    Corrections = [new InventoryPhysicalCountCorrectionDto
                    {
                        LineNo = 1, Quantity = -2m, Reason = "One additional damaged item found behind cabinet"
                    }]
                });
            Assert.Equal(200, review.StatusCode);
            Assert.Equal(-2m, Assert.Single(review.Data!.Lines).PostedVariance);
            Assert.NotNull(Assert.Single(review.Data.Lines).CorrectionReason);
            Assert.Equal(8m, (await CountChecker(checkerDb, f, reviewerId)
                .GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        }
        InventoryPhysicalCountDto second;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
            second = await OpenAndSubmitCountAsync(makerDb, f, 7m);
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var review = await CountChecker(checkerDb, f, reviewerId).ReviewPhysicalCountAsync(second.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = second.RowVersion,
                    Decision = "Approve", VarianceMode = "AutoPlusManual", Reason = "Reconciliation includes tagged backroom stock",
                    Corrections = [new InventoryPhysicalCountCorrectionDto
                    {
                        LineNo = 1, Quantity = 2m, Reason = "Two boxes counted in sealed backroom"
                    }]
                });
            Assert.Equal(200, review.StatusCode);
            Assert.Equal(1m, Assert.Single(review.Data!.Lines).PostedVariance);
            Assert.Equal(9m, (await CountChecker(checkerDb, f, reviewerId)
                .GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
            Assert.Equal(2, await checkerDb.InventoryMovements.CountAsync(x => x.SourceType == "PhysicalStockCount"));
        }
    }

    [Fact]
    public async Task Zero_book_surplus_needs_cost_and_reserved_stock_cannot_be_consumed()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var reviewerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto surplus;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
            surplus = await OpenAndSubmitCountAsync(makerDb, f, 5m, f.DestinationId);
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var checker = CountChecker(checkerDb, f, reviewerId);
            var request = new ReviewInventoryPhysicalCountRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RowVersion = surplus.RowVersion,
                Decision = "Approve", VarianceMode = "Automatic", Reason = "Found unrecorded sealed shipment in store"
            };
            Assert.Equal(400, (await checker.ReviewPhysicalCountAsync(surplus.Reference, request)).StatusCode);
            request.Corrections = [new InventoryPhysicalCountCorrectionDto
            {
                LineNo = 1, Quantity = 0m, ReceiptUnitCost = 4m,
                Reason = "Unrecorded shipment valued from purchase invoice"
            }];
            Assert.Equal(200, (await checker.ReviewPhysicalCountAsync(surplus.Reference, request)).StatusCode);
            Assert.Equal(5m, (await checker.GetItemBalanceAsync(f.ItemId, f.DestinationId, null)).Data!.QuantityOnHand);
        }

        InventoryPhysicalCountDto shortage;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
        {
            var hold = await DraftIssueAsync(makerDb, f, 8m);
            Assert.Equal(200, (await Service(makerDb, f).ReserveMovementAsync(hold.Reference,
                new PostInventoryMovementRequestDto { RowVersion = hold.RowVersion })).StatusCode);
            shortage = await OpenAndSubmitCountAsync(makerDb, f, 5m);
        }
        await using (var checkerDb = OpenAs(f.Options, f.TenantId, reviewerId))
        {
            var checker = CountChecker(checkerDb, f, reviewerId);
            Assert.Equal(409, (await checker.ReviewPhysicalCountAsync(shortage.Reference,
                new ReviewInventoryPhysicalCountRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), RowVersion = shortage.RowVersion,
                    Reason = "Shortage cannot consume separately reserved items"
                })).StatusCode);
            Assert.Equal(10m, (await checker.GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
        }
    }

    [Fact]
    public async Task Count_rejects_foreign_tenant_role_and_changed_start_request()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var start = CountRequest(f);
        await using var makerDb = OpenAs(f.Options, f.TenantId, f.UserId);
        var maker = Service(makerDb, f);
        var created = await maker.StartPhysicalCountAsync(start);
        Assert.Equal(200, created.StatusCode);
        var reference = created.Data!.Reference;
        Assert.Equal(200, (await maker.StartPhysicalCountAsync(start)).StatusCode);
        start.Reason = "Different request data with same idempotency key";
        Assert.Equal(409, (await maker.StartPhysicalCountAsync(start)).StatusCode);
        Assert.Equal(403, (await Service(makerDb, f, "Student").GetPhysicalCountsAsync(null, null, 1, 25)).StatusCode);
        await using var foreign = Open(f.Options, f.TenantId + 888);
        Assert.Equal(404, (await Service(foreign, f, tenantId: f.TenantId + 888)
            .GetPhysicalCountAsync(reference)).StatusCode);
        Assert.Equal(403, (await Service(foreign, f, tenantId: f.TenantId + 888)
            .StartPhysicalCountAsync(CountRequest(f))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_checkers_cannot_double_post_physical_variance()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var checkerId = await NewCountCheckerAsync(f);
        InventoryPhysicalCountDto submitted;
        await using (var makerDb = OpenAs(f.Options, f.TenantId, f.UserId))
            submitted = await OpenAndSubmitCountAsync(makerDb, f, 7m);
        await using var first = OpenAs(f.Options, f.TenantId, checkerId);
        await using var second = OpenAs(f.Options, f.TenantId, checkerId);
        ReviewInventoryPhysicalCountRequestDto Command() => new()
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
            Decision = "Approve", VarianceMode = "Automatic", Reason = "Verified physical shortage during full review"
        };
        var a = CountChecker(first, f, checkerId).ReviewPhysicalCountAsync(submitted.Reference, Command());
        var b = CountChecker(second, f, checkerId).ReviewPhysicalCountAsync(submitted.Reference, Command());
        var results = await Task.WhenAll(a, b);
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.TenantId);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.SourceType == "PhysicalStockCount"));
        Assert.Equal(7m, (await Service(verify, f).GetItemBalanceAsync(f.ItemId, f.SourceId, null)).Data!.QuantityOnHand);
    }

    [Fact]
    public async Task Scoped_staff_cannot_guess_other_campus_count_and_sql_guards_book_snapshot()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = OpenAs(f.Options, f.TenantId, f.UserId);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var allowed = new Campus { TenantId = f.TenantId, Code = "COUNT-A-" + suffix, Name = "Count campus A" };
        var forbidden = new Campus { TenantId = f.TenantId, Code = "COUNT-B-" + suffix, Name = "Count campus B" };
        db.Campuses.AddRange(allowed, forbidden);
        await db.SaveChangesAsync();
        (await db.InventoryLocations.SingleAsync(x => x.Id == f.SourceId)).CampusId = allowed.Id;
        (await db.InventoryLocations.SingleAsync(x => x.Id == f.DestinationId)).CampusId = forbidden.Id;
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = f.TenantId, UserId = f.UserId, CampusId = allowed.Id, IsActive = true
        });
        await db.SaveChangesAsync();
        var admin = Service(db, f);
        var hidden = await admin.StartPhysicalCountAsync(CountRequest(f, f.DestinationId));
        Assert.Equal(200, hidden.StatusCode);
        var staff = Service(db, f, "StoreKeeper");
        Assert.Equal(403, (await staff.StartPhysicalCountAsync(CountRequest(f, f.DestinationId))).StatusCode);
        Assert.Equal(404, (await staff.GetPhysicalCountAsync(hidden.Data!.Reference)).StatusCode);
        var list = await staff.GetPhysicalCountsAsync(null, null, 1, 25);
        Assert.DoesNotContain(list.Data!.Items, x => x.Reference == hidden.Data.Reference);
        var source = await staff.StartPhysicalCountAsync(CountRequest(f));
        Assert.Equal(200, source.StatusCode);
        var snapshotId = await db.InventoryPhysicalCountLines.Where(x =>
            x.InventoryPhysicalCountId == db.InventoryPhysicalCounts.Where(c =>
                c.PublicId == source.Data!.Reference).Select(c => c.Id).Single()).Select(x => x.Id).SingleAsync();
        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.InventoryPhysicalCountLines SET BookQuantity=99 WHERE Id={snapshotId}"));
        Assert.Equal(51112, error.Number);
    }
}
