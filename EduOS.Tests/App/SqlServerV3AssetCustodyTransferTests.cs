using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
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
public sealed class SqlServerV3AssetCustodyTransferTests
{
    private sealed class Actor(long tenant, long user, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Asset transfer tester";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private sealed record Fixture(DbContextOptions<EduOSDbContext> Options, long Tenant,
        long User, long Assignment, long FromUnit, long ToUnit, long OtherCampusUnit,
        Guid AssetReference, string RowVersion);

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static AssetLifecycleService Service(EduOSDbContext db, Fixture f,
        string role = "TenantAdmin", long? tenant = null) =>
        new(db, new Actor(tenant ?? f.Tenant, f.User, role),
            TimeProvider.System, NullLogger<AssetLifecycleService>.Instance);

    private static async Task<Fixture> SeedAsync(string connection)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, userId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "AS-TR-" + suffix, Name = "Asset transfer SQL",
                Email = "asset-" + suffix + "@example.test", State = TenantState.Active
            };
            var account = new ApplicationUser
            {
                UserName = "asset-" + suffix,
                NormalizedUserName = ("asset-" + suffix).ToUpperInvariant(),
                Email = "asset-" + suffix + "@example.test",
                NormalizedEmail = ("asset-" + suffix + "@example.test").ToUpperInvariant(),
                FullName = "Asset transfer tester"
            };
            root.Tenants.Add(tenant);
            root.ApplicationUsers.Add(account);
            await root.SaveChangesAsync();
            tenantId = tenant.Id; userId = account.Id;
        }
        await using var db = Open(options, tenantId);
        var campus = new Campus { TenantId = tenantId, Code = "A-" + suffix, Name = "Asset campus", IsActive = true };
        var otherCampus = new Campus { TenantId = tenantId, Code = "B-" + suffix, Name = "Other campus", IsActive = true };
        var category = new AssetCategory { TenantId = tenantId, Code = "CAT-" + suffix, Name = "Office assets" };
        db.Campuses.AddRange(campus, otherCampus);
        db.AssetCategories.Add(category);
        await db.SaveChangesAsync();
        var from = new OrganizationUnit { TenantId = tenantId, CampusId = campus.Id, Code = "FROM-" + suffix, Name = "Office A" };
        var to = new OrganizationUnit { TenantId = tenantId, CampusId = campus.Id, Code = "TO-" + suffix, Name = "Office B" };
        var blocked = new OrganizationUnit { TenantId = tenantId, CampusId = otherCampus.Id, Code = "OTHER-" + suffix, Name = "Other campus unit" };
        db.OrganizationUnits.AddRange(from, to, blocked);
        var asset = new Asset
        {
            TenantId = tenantId, CampusId = campus.Id, AssetCategoryId = category.Id,
            AssetTag = "TAG-" + suffix, Name = "Office laptop", StateCode = "Assigned"
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        var assignment = new AssetAssignment
        {
            TenantId = tenantId, AssetId = asset.Id, OrganizationUnitId = from.Id,
            ClientRequestId = Guid.NewGuid(), AssignedOn = new DateOnly(2026, 1, 1),
            StateCode = "Assigned"
        };
        db.AssetAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return new Fixture(options, tenantId, userId, assignment.Id, from.Id, to.Id, blocked.Id,
            asset.PublicId, Convert.ToBase64String(assignment.RowVersion));
    }

    private static TransferAssetRequestDto Request(Fixture f, Guid key, long? unit = null) => new()
    {
        ClientRequestId = key, TransferredOn = new DateOnly(2026, 10, 20),
        RowVersion = f.RowVersion, OrganizationUnitId = unit ?? f.ToUnit,
        Reason = "Approved custodian change"
    };


    [Fact]
    public async Task Capitalization_preflight_requires_exact_posted_receipt_stock_line_and_is_tenant_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = Open(f.Options, f.Tenant);
        var asset = await db.Assets.SingleAsync(x => x.PublicId == f.AssetReference);
        var campusId = asset.CampusId!.Value;
        var item = new InventoryItem { TenantId = f.Tenant, Code = "CAP-I-" + suffix,
            Name = "Tracked microscope", IsActive = true, IsStockTracked = true, IsAssetTracked = true };
        var location = new InventoryLocation { TenantId = f.Tenant, CampusId = campusId,
            Code = "CAP-L-" + suffix, Name = "Receiving store", IsActive = true };
        var supplier = new ProcurementSupplier { TenantId = f.Tenant, CampusId = campusId,
            ClientRequestId = Guid.NewGuid(), SupplierCode = "CAP-S-" + suffix,
            Name = "Supplier", StateCode = "Approved" };
        var requisition = new ProcurementRequisition { TenantId = f.Tenant, CampusId = campusId,
            ClientRequestId = Guid.NewGuid(), RequestNumber = "CAP-R-" + suffix,
            Purpose = "Laboratory capital equipment", StateCode = "Approved" };
        db.AddRange(item, location, supplier, requisition);
        await db.SaveChangesAsync();
        var requisitionLine = new ProcurementRequisitionLine { TenantId = f.Tenant,
            RequisitionId = requisition.Id, LineNo = 1, InventoryItemId = item.Id,
            Quantity = 1m, EstimatedUnitPrice = 120m };
        var order = new ProcurementPurchaseOrder { TenantId = f.Tenant, CampusId = campusId,
            ClientRequestId = Guid.NewGuid(), OrderNumber = "CAP-PO-" + suffix,
            RequisitionId = requisition.Id, SupplierId = supplier.Id, StateCode = "Approved" };
        db.AddRange(requisitionLine, order);
        await db.SaveChangesAsync();
        var orderLine = new ProcurementPurchaseOrderLine { TenantId = f.Tenant,
            PurchaseOrderId = order.Id, RequisitionLineId = requisitionLine.Id, LineNo = 1,
            InventoryItemId = item.Id, Quantity = 1m, UnitPrice = 120m };
        db.ProcurementPurchaseOrderLines.Add(orderLine);
        await db.SaveChangesAsync();
        var reviewer = new ApplicationUser {
            UserName = "cap-review-" + suffix,
            NormalizedUserName = ("cap-review-" + suffix).ToUpperInvariant(),
            Email = "cap-review-" + suffix + "@example.test",
            NormalizedEmail = ("cap-review-" + suffix + "@example.test").ToUpperInvariant(),
            FullName = "Independent GR reviewer" };
        db.ApplicationUsers.Add(reviewer);
        await db.SaveChangesAsync();
        var reviewedAt = DateTime.UtcNow;
        var receipt = new ProcurementGoodsReceipt { TenantId = f.Tenant,
            ClientRequestId = Guid.NewGuid(), PurchaseOrderId = order.Id,
            CampusId = campusId, LocationId = location.Id, StateCode = "Posted",
            Reason = "Independently reviewed delivery",
            SubmitRequestId = Guid.NewGuid(), SubmittedAtUtc = reviewedAt.AddMinutes(-1),
            SubmittedByUserId = f.User, ReviewRequestId = Guid.NewGuid(),
            ReviewReason = "Independently approved delivery",
            ReviewedAtUtc = reviewedAt, ReviewedByUserId = reviewer.Id,
            ValuationMode = "Automatic",
            CreateFingerprint = new string('A', 64) };
        db.ProcurementGoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();
        var movement = new InventoryMovement { TenantId = f.Tenant,
            ClientRequestId = Guid.NewGuid(), MovementNumber = "CAP-M-" + suffix,
            MovementTypeCode = "Receipt", StateCode = "Posted", ToLocationId = location.Id,
            SourceType = "ProcurementGoodsReceipt", SourceId = receipt.Id,
            PostedAt = reviewedAt, PostedByUserId = reviewer.Id };
        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync();
        db.ProcurementGoodsReceiptLines.Add(new ProcurementGoodsReceiptLine {
            TenantId = f.Tenant, GoodsReceiptId = receipt.Id, LineNo = 1,
            PurchaseOrderLineId = orderLine.Id, InventoryItemId = item.Id,
            LotOrBatchNo = "CAP-LOT", Quantity = 1m, ApprovedPoQuantity = 1m,
            ApprovedPoUnitPrice = 120m, PostedUnitCost = 120m });
        var stockLine = new InventoryMovementLine { TenantId = f.Tenant,
            InventoryMovementId = movement.Id, LineNo = 1, InventoryItemId = item.Id,
            LotOrBatchNo = "CAP-LOT", Quantity = 1m, UnitCost = 120m };
        db.InventoryMovementLines.Add(stockLine);
        receipt.PostedMovementId = movement.Id;
        asset.InventoryItemId = item.Id;
        asset.InventoryLocationId = location.Id;
        asset.PurchaseCost = 120m;
        await db.SaveChangesAsync();

        // Seed the historic maker only after all tracked receipt writes. ExecuteUpdate
        // advances SQL rowversion, so updating it earlier makes the subsequent
        // PostedMovementId SaveChanges fail optimistic concurrency.
        // The disposable test context has no authenticated principal. SaveChanges
        // stamps CreatedBy from that context, so an initializer value is discarded.
        // Persist the historical GR maker explicitly; the independent checker
        // and stock-posting evidence remain subject to the real service guard.
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, (long?)f.User));
        Assert.Equal((long?)f.User, await db.ProcurementGoodsReceipts.AsNoTracking()
            .Where(x => x.Id == receipt.Id).Select(x => x.CreatedBy).SingleAsync());

        var service = Service(db, f);
        async Task<AssetCapitalizationReadinessDto> Read()
        {
            var response = await service.GetCapitalizationReadinessAsync(f.AssetReference);
            Assert.Equal(200, response.StatusCode);
            return response.Data!;
        }
        var valid = await Read();
        Assert.Equal("ReviewRequired", valid.StatusCode);
        Assert.Equal(movement.PublicId, Assert.Single(valid.Candidates).PostedMovementReference);
        Assert.Equal(1, valid.CandidateLineCount);
        // A posted stock line alone is insufficient: verify the approved PO source
        // and immutable line-cost/quantity snapshot before suggesting capitalization.
        await db.ProcurementGoodsReceiptLines.Where(x => x.GoodsReceiptId == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ApprovedPoUnitPrice, 999m));
        Assert.Equal("NoPostedReceiptEvidence", (await Read()).StatusCode);
        await db.ProcurementGoodsReceiptLines.Where(x => x.GoodsReceiptId == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ApprovedPoUnitPrice, 120m));
        await db.ProcurementPurchaseOrderLines.Where(x => x.Id == orderLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitPrice, 121m));
        Assert.Empty((await Read()).Candidates);
        await db.ProcurementPurchaseOrderLines.Where(x => x.Id == orderLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitPrice, 120m));
        await db.ProcurementPurchaseOrders.Where(x => x.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StateCode, "Draft"));
        Assert.Empty((await Read()).Candidates);
        await db.ProcurementPurchaseOrders.Where(x => x.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StateCode, "Approved"));
        Assert.Single((await Read()).Candidates);
        // Posted labels with missing approval or forged maker/checker are excluded.
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewedByUserId, (long?)f.User));
        Assert.Empty((await Read()).Candidates);
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewedByUserId, (long?)reviewer.Id));
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValuationMode, "Unapproved"));
        Assert.Equal("NoPostedReceiptEvidence", (await Read()).StatusCode);
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValuationMode, "Automatic"));
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewRequestId, (Guid?)null));
        Assert.Empty((await Read()).Candidates);
        await db.ProcurementGoodsReceipts.Where(x => x.Id == receipt.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewRequestId, (Guid?)Guid.NewGuid()));
        await db.InventoryMovements.Where(x => x.Id == movement.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PostedByUserId, (long?)f.User));
        Assert.Empty((await Read()).Candidates);
        await db.InventoryMovements.Where(x => x.Id == movement.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PostedByUserId, (long?)reviewer.Id));
        Assert.Single((await Read()).Candidates);

        await db.InventoryMovementLines.Where(x => x.Id == stockLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitCost, 121m));
        Assert.Empty((await Read()).Candidates);
        await db.InventoryMovementLines.Where(x => x.Id == stockLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitCost, 120m));
        await db.InventoryMovementLines.Where(x => x.Id == stockLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LotOrBatchNo, "WRONG-LOT"));
        Assert.Equal("NoPostedReceiptEvidence", (await Read()).StatusCode);
        await db.InventoryMovementLines.Where(x => x.Id == stockLine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LotOrBatchNo, "CAP-LOT"));
        await db.InventoryMovements.Where(x => x.Id == movement.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ToLocationId, (long?)null));
        Assert.Equal(0, (await Read()).CandidateLineCount);
        await db.InventoryMovements.Where(x => x.Id == movement.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ToLocationId, (long?)location.Id));
        Assert.Equal(1, (await Read()).CandidateLineCount);

        await using var foreign = Open(f.Options, f.Tenant + 999);
        Assert.Equal(404, (await Service(foreign, f, tenant: f.Tenant + 999)
            .GetCapitalizationReadinessAsync(f.AssetReference)).StatusCode);
    }

    [Fact]
    public async Task Transfer_is_atomic_idempotent_and_preserves_both_custodians()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.Tenant);
        var service = Service(db, f);
        var key = Guid.NewGuid();
        var first = await service.TransferAssetAsync(f.Assignment, Request(f, key));
        Assert.Equal(200, first.StatusCode);
        Assert.Equal(f.ToUnit, first.Data!.OrganizationUnitId);
        var replay = await service.TransferAssetAsync(f.Assignment, Request(f, key));
        Assert.Equal(200, replay.StatusCode);
        Assert.Equal(first.Data.Id, replay.Data!.Id);
        Assert.Equal(409, (await service.TransferAssetAsync(f.Assignment,
            Request(f, key, f.FromUnit))).StatusCode);
        var rows = await db.AssetAssignments.AsNoTracking().Where(x =>
            x.TenantId == f.Tenant && x.AssetId == db.Assets.Where(a =>
                a.PublicId == f.AssetReference).Select(a => a.Id).First()).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, x => x.StateCode == "Assigned" && x.ReturnedOn == null);
        Assert.Single(rows, x => x.StateCode == "Transferred" && x.ReturnedOn.HasValue);
        Assert.Contains("Approved custodian change", rows.Single(x => x.StateCode == "Transferred").Remarks);
    }

    [Fact]
    public async Task Invalid_campus_role_and_guessed_assignment_do_not_mutate_custody()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var db = Open(f.Options, f.Tenant);
        Assert.Equal(400, (await Service(db, f).TransferAssetAsync(f.Assignment,
            Request(f, Guid.NewGuid(), f.OtherCampusUnit))).StatusCode);
        Assert.Equal(403, (await Service(db, f, "Student").TransferAssetAsync(f.Assignment,
            Request(f, Guid.NewGuid()))).StatusCode);
        Assert.Equal(404, (await Service(db, f).TransferAssetAsync(long.MaxValue,
            Request(f, Guid.NewGuid()))).StatusCode);
        await using var foreign = Open(f.Options, f.Tenant + 999);
        Assert.Equal(404, (await Service(foreign, f, tenant: f.Tenant + 999).TransferAssetAsync(
            f.Assignment, Request(f, Guid.NewGuid()))).StatusCode);
        // HttpContextAccessor uses an ambient AsyncLocal; restore the original tenant
        // explicitly after probing a foreign tenant in the same async test flow.
        await using var verify = Open(f.Options, f.Tenant);
        Assert.Equal("Assigned", (await verify.AssetAssignments.AsNoTracking().SingleAsync(
            x => x.Id == f.Assignment)).StateCode);
    }

    [Fact]
    public async Task Competing_transfers_cannot_leave_two_active_custodians()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        await using var firstDb = Open(f.Options, f.Tenant);
        await using var secondDb = Open(f.Options, f.Tenant);
        var a = Service(firstDb, f).TransferAssetAsync(f.Assignment, Request(f, Guid.NewGuid()));
        var b = Service(secondDb, f).TransferAssetAsync(f.Assignment, Request(f, Guid.NewGuid()));
        var results = await Task.WhenAll(a, b);
        Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
        await using var verify = Open(f.Options, f.Tenant);
        var assetId = await verify.Assets.Where(x => x.PublicId == f.AssetReference).Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await verify.AssetAssignments.CountAsync(x =>
            x.AssetId == assetId && !x.IsDeleted && x.ReturnedOn == null));
    }
    [Fact]
    public async Task Unassigned_booked_asset_freezes_source_identity_and_valuation_without_blocking_metadata()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var f = await SeedAsync(connection);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        Guid bookedRef, unbookedRef;
        long otherCategoryId, otherCampusId;

        await using (var seed = Open(f.Options, f.Tenant))
        {
            var original = await seed.Assets.AsNoTracking()
                .SingleAsync(x => x.PublicId == f.AssetReference);
            var campusId = original.CampusId!.Value;
            otherCampusId = (await seed.OrganizationUnits.AsNoTracking()
                .SingleAsync(x => x.Id == f.OtherCampusUnit)).CampusId!.Value;
            var category = new AssetCategory { TenantId = f.Tenant,
                Code = "BOOK-C-" + suffix, Name = "Other asset category" };
            var item = new InventoryItem { TenantId = f.Tenant,
                Code = "BOOK-I-" + suffix, Name = "Capitalized equipment", IsAssetTracked = true };
            var location = new InventoryLocation { TenantId = f.Tenant,
                CampusId = campusId, Code = "BOOK-L-" + suffix, Name = "Asset storage" };
            seed.AssetCategories.Add(category);
            seed.InventoryItems.Add(item);
            seed.InventoryLocations.Add(location);
            await seed.SaveChangesAsync();
            otherCategoryId = category.Id;
            var booked = new Asset { TenantId = f.Tenant, CampusId = campusId,
                AssetCategoryId = original.AssetCategoryId, InventoryItemId = item.Id,
                InventoryLocationId = location.Id, AssetTag = ("BOOKED-" + suffix).ToUpperInvariant(),
                Name = "Capitalized microscope", PurchaseDate = new DateOnly(2026, 10, 1),
                PurchaseCost = 1000m };
            var unbooked = new Asset { TenantId = f.Tenant, CampusId = campusId,
                AssetCategoryId = original.AssetCategoryId, InventoryItemId = item.Id,
                InventoryLocationId = location.Id, AssetTag = ("UNBOOKED-" + suffix).ToUpperInvariant(),
                Name = "Unbooked microscope", PurchaseDate = new DateOnly(2026, 10, 1),
                PurchaseCost = 1000m };
            seed.Assets.AddRange(booked, unbooked);
            await seed.SaveChangesAsync();
            bookedRef = booked.PublicId;
            unbookedRef = unbooked.PublicId;
            seed.AssetBooks.Add(new AssetBook { TenantId = f.Tenant, AssetId = booked.Id,
                CampusId = campusId, InServiceDate = new DateOnly(2026, 10, 1),
                MethodCode = "StraightLine", UsefulLifeMonths = 36, CostBasis = 1000m,
                SalvageValue = 100m, PolicyVersion = 1, IsActive = true });
            await seed.SaveChangesAsync();
            Assert.False(await seed.AssetAssignments.AnyAsync(x => x.AssetId == booked.Id));
        }

        await using (var db = Open(f.Options, f.Tenant))
        {
            var service = Service(db, f);
            var booked = await db.Assets.AsNoTracking()
                .SingleAsync(x => x.PublicId == bookedRef);
            SaveAssetRequestDto Request(Asset a) => new()
            {
                AssetCategoryId = a.AssetCategoryId, CampusId = a.CampusId,
                InventoryItemId = a.InventoryItemId, InventoryLocationId = a.InventoryLocationId,
                AssetTag = a.AssetTag, Name = a.Name, SerialNumber = a.SerialNumber,
                PurchaseDate = a.PurchaseDate, PurchaseCost = a.PurchaseCost,
                IsActive = a.IsActive, RowVersion = Convert.ToBase64String(a.RowVersion)
            };

            // Display metadata remains editable; the SQL rowversion advances.
            var metadata = Request(booked);
            metadata.Name = "Microscope — renamed";
            var renamed = await service.SaveAssetAsync(bookedRef, metadata);
            Assert.True(renamed.StatusCode == 200, $"Metadata edit failed: {renamed.StatusCode} {renamed.Message}");
            booked = await db.Assets.AsNoTracking().SingleAsync(x => x.PublicId == bookedRef);
            Action<SaveAssetRequestDto>[] forbidden =
            [
                x => x.PurchaseCost = 1500m,
                x => x.PurchaseDate = new DateOnly(2026, 10, 2),
                x => x.AssetTag = "RETAG-" + suffix,
                x => x.AssetCategoryId = otherCategoryId,
                x => x.InventoryItemId = null,
                x => x.InventoryLocationId = null,
                x => { x.CampusId = otherCampusId; x.InventoryLocationId = null; }
            ];
            foreach (var edit in forbidden)
            {
                var request = Request(booked);
                edit(request);
                Assert.Equal(409, (await service.SaveAssetAsync(bookedRef, request)).StatusCode);
            }
            var after = await db.Assets.AsNoTracking().SingleAsync(x => x.PublicId == bookedRef);
            Assert.Equal(1000m, after.PurchaseCost);
            Assert.Equal(booked.CampusId, after.CampusId);
            Assert.Equal(booked.AssetTag, after.AssetTag);
            Assert.Equal(booked.PurchaseDate, after.PurchaseDate);
            Assert.Equal(1000m, (await db.AssetBooks.AsNoTracking()
                .SingleAsync(x => x.AssetId == booked.Id)).CostBasis);

            // An unassigned asset with no book retains the normal draft edit path.
            var draft = await db.Assets.AsNoTracking().SingleAsync(x => x.PublicId == unbookedRef);
            var draftChange = Request(draft);
            draftChange.PurchaseCost = 1200m;
            Assert.Equal(200, (await service.SaveAssetAsync(unbookedRef, draftChange)).StatusCode);
            Assert.Equal(1200m, (await db.Assets.AsNoTracking()
                .SingleAsync(x => x.PublicId == unbookedRef)).PurchaseCost);
        }
    }

}
