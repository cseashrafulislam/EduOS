using EduOS.Core.DTOs.Facilities;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Accounting;
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
        long periodId, expenseId, assetAccountId;
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
                var day = DateOnly.FromDateTime(DateTime.UtcNow);
                var period = new AccountingPeriod { TenantId = tenantId, Name = "Facility Finance " + suffix,
                    StartDate = day.AddDays(-5), EndDate = day.AddDays(5), IsClosed = false };
                var repairs = new Account { TenantId = tenantId, Code = "REPAIR-" + suffix,
                    Name = "Facility Repairs", AccountType = AccountType.Expense, AllowsPosting = true };
                var stockAsset = new Account { TenantId = tenantId, Code = "STOCK-" + suffix,
                    Name = "Inventory Asset", AccountType = AccountType.Asset, AllowsPosting = true };
                setup.AddRange(period, repairs, stockAsset);
                setup.UserCampusAccesses.AddRange(new UserCampusAccess {
                    TenantId = tenantId, CampusId = campusId, UserId = checkerId, IsActive = true
                }, new UserCampusAccess {
                    TenantId = tenantId, CampusId = campusId, UserId = managerId, IsActive = true
                });
                await setup.SaveChangesAsync();
                stockLocationId = location.Id; itemId = item.Id;
                periodId = period.Id; expenseId = repairs.Id; assetAccountId = stockAsset.Id;
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
        Guid financeMovement;
        string financeMovementVersion;
        await using (var issueDb = Db(options, tenantId, makerId, "StoreKeeper"))
        {
            input.ClientRequestId = Guid.NewGuid();
            var issue = await Inventory(issueDb, makerId, "StoreKeeper")
                .CreateFacilityPartsIssueAsync(requestId, input);
            Assert.True(issue.Success, issue.Message);
            financeMovement = issue.Data!.Reference;
            financeMovementVersion = issue.Data.RowVersion;
        }
        await using (var postDb = Db(options, tenantId, checkerId, "InventoryManager"))
        {
            var issue = await Inventory(postDb, checkerId, "InventoryManager")
                .PostMovementAsync(financeMovement, new PostInventoryMovementRequestDto { RowVersion = financeMovementVersion });
            Assert.True(issue.Success, issue.Message);
        }
        FacilityPartsFinanceService Finance(EduOSDbContext db, long actor, string role) =>
            new(db, new Principal(tenantId, actor, role), TimeProvider.System,
                Inventory(db, actor, role), NullLogger<FacilityPartsFinanceService>.Instance);
        var dayOfJournal = DateOnly.FromDateTime(DateTime.UtcNow);
        var financeInput = new FacilityPartsFinanceRequestDto { ClientRequestId = Guid.NewGuid(),
            AccountingPeriodId = periodId, JournalDate = dayOfJournal,
            RepairsExpenseAccountId = expenseId, InventoryAssetAccountId = assetAccountId,
            Reason = "Approved filter replacement charged to Facility repairs" };
        Guid journalRef;
        string financeVersion;
        await using (var makerDb = Db(options, tenantId, makerId, "Accountant"))
        {
            var service = Finance(makerDb, makerId, "Accountant");
            var requestFinance = await service.RequestAsync(financeMovement, financeInput);
            Assert.True(requestFinance.Success, requestFinance.Message);
            Assert.Equal("Submitted", requestFinance.Data!.State);
            Assert.Equal(25m, requestFinance.Data.AmountBdt);
            journalRef = requestFinance.Data.JournalReference;
            financeVersion = requestFinance.Data.RowVersion;
            var replay = await service.RequestAsync(financeMovement, financeInput);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(journalRef, replay.Data!.JournalReference);
            var conflicting = new FacilityPartsFinanceRequestDto {
                ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                JournalDate = dayOfJournal, RepairsExpenseAccountId = expenseId,
                InventoryAssetAccountId = assetAccountId,
                Reason = "Another expense request for the exact same stock issue" };
            Assert.Equal(409, (await service.RequestAsync(financeMovement, conflicting)).StatusCode);
            Assert.Equal(403, (await service.ReviewAsync(journalRef,
                new FacilityPartsFinanceReviewDto { RowVersion = financeVersion, Approve = true })).StatusCode);
        }
        await using (var samePersonDb = Db(options, tenantId, makerId, "TenantAdmin"))
            Assert.Equal(403, (await Finance(samePersonDb, makerId, "TenantAdmin")
                .ReviewAsync(journalRef, new FacilityPartsFinanceReviewDto {
                    RowVersion = financeVersion, Approve = true })).StatusCode);
        await using (var differentTenantDb = Db(options, tenantId + 100000, makerId, "TenantAdmin"))
            Assert.Equal(404, (await Finance(differentTenantDb, makerId, "TenantAdmin")
                .GetAsync(financeMovement)).StatusCode);
        await using (var reviewerDb = Db(options, tenantId, managerId, "Principal"))
        {
            var finance = Finance(reviewerDb, managerId, "Principal");
            var approved = await finance.ReviewAsync(journalRef,
                new FacilityPartsFinanceReviewDto { RowVersion = financeVersion, Approve = true });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Posted", approved.Data!.State);
            var journal = await reviewerDb.Journals.AsNoTracking().SingleAsync(j =>
                j.TenantId == tenantId && j.PublicId == journalRef);
            var lines = await reviewerDb.JournalLines.AsNoTracking().Where(l =>
                l.TenantId == tenantId && l.JournalId == journal.Id).OrderBy(l => l.LineNo).ToListAsync();
            Assert.Equal(2, lines.Count);
            Assert.Equal((expenseId, 25m, 0m), (lines[0].AccountId, lines[0].DebitAmount, lines[0].CreditAmount));
            Assert.Equal((assetAccountId, 0m, 25m), (lines[1].AccountId, lines[1].DebitAmount, lines[1].CreditAmount));
            var stock = await Inventory(reviewerDb, managerId, "Principal").GetMovementAsync(financeMovement);
            Assert.Equal(409, (await Inventory(reviewerDb, managerId, "Principal")
                .ReverseMovementAsync(financeMovement, new ReverseInventoryMovementRequestDto {
                    ClientRequestId = Guid.NewGuid(), RowVersion = stock.Data!.RowVersion,
                    Reason = "Finance linked stock must reverse together" })).StatusCode);
        }
        Guid jointReversal;
        string jointVersion;
        await using (var makerDb = Db(options, tenantId, makerId, "Accountant"))
        {
            var reversalRequest = await Finance(makerDb, makerId, "Accountant")
                .RequestReversalAsync(journalRef, new FacilityPartsFinanceReversalRequestDto {
                    ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                    JournalDate = dayOfJournal, Reason = "Unused filter parts returned after correction" });
            Assert.True(reversalRequest.Success, reversalRequest.Message);
            Assert.Equal("Submitted", reversalRequest.Data!.ReversalState);
            jointReversal = reversalRequest.Data.FinanceReversalReference!.Value;
            jointVersion = reversalRequest.Data.ReversalRowVersion!;
        }
        await using (var reviewerDb = Db(options, tenantId, managerId, "Principal"))
        {
            var approved = await Finance(reviewerDb, managerId, "Principal")
                .ReviewReversalAsync(jointReversal, new FacilityPartsFinanceReviewDto {
                    RowVersion = jointVersion, Approve = true });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Reversed", approved.Data!.State);
            Assert.Equal("Posted", approved.Data.ReversalState);
            Assert.NotNull(approved.Data.StockReversalReference);
            var balance = await Inventory(reviewerDb, managerId, "Principal")
                .GetItemBalanceAsync(itemId, stockLocationId, null);
            Assert.Equal(10m, balance.Data!.QuantityOnHand);
            var journal = await reviewerDb.Journals.AsNoTracking().SingleAsync(j =>
                j.TenantId == tenantId && j.PublicId == jointReversal);
            var entries = await reviewerDb.JournalLines.AsNoTracking().Where(l =>
                l.TenantId == tenantId && l.JournalId == journal.Id).OrderBy(l => l.LineNo).ToListAsync();
            Assert.Equal((expenseId, 0m, 25m), (entries[0].AccountId, entries[0].DebitAmount, entries[0].CreditAmount));
            Assert.Equal((assetAccountId, 25m, 0m), (entries[1].AccountId, entries[1].DebitAmount, entries[1].CreditAmount));
            var report = await Service(reviewerDb, tenantId, managerId, "Principal")
                .PartsAsync(requestId, 1, 25);
            Assert.Contains(report.Data!.Items, x => x.Reference == financeMovement &&
                x.FinanceState == "Reversed" && x.NetCost == 0m && x.FinanceReversalReference == jointReversal);
        }
        await using (var makerDb = Db(options, tenantId, makerId, "Accountant"))
            Assert.Equal(409, (await Finance(makerDb, makerId, "Accountant")
                .RequestReversalAsync(journalRef, new FacilityPartsFinanceReversalRequestDto {
                    ClientRequestId = Guid.NewGuid(), AccountingPeriodId = periodId,
                    JournalDate = dayOfJournal, Reason = "Cannot reverse the same stock issue a second time" })).StatusCode);
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
            Assert.Equal(3, report.Data!.TotalCount);
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
