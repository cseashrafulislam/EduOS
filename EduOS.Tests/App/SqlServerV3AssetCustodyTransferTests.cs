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
                InventoryLocationId = location.Id, AssetTag = "BOOKED-" + suffix,
                Name = "Capitalized microscope", PurchaseDate = new DateOnly(2026, 10, 1),
                PurchaseCost = 1000m };
            var unbooked = new Asset { TenantId = f.Tenant, CampusId = campusId,
                AssetCategoryId = original.AssetCategoryId, InventoryItemId = item.Id,
                InventoryLocationId = location.Id, AssetTag = "UNBOOKED-" + suffix,
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
            Assert.Equal(200, renamed.StatusCode);
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
