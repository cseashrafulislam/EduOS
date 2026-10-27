using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using EduOS.Core.Entities.Inventory;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssetAccountingPersistenceContractTests
{
    [Fact]
    public void Asset_accounting_records_have_tenant_bound_relationships_and_rowversion()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=localhost;Database=EduOS_Model_Inspection;TrustServerCertificate=True;Integrated Security=True").Options;
        using var db = new EduOSDbContext(options);
        foreach (var type in new[] { typeof(AssetBook), typeof(AssetDepreciationRun),
            typeof(AssetDepreciationLine), typeof(AssetImpairment), typeof(AssetDisposal) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.NotNull(entity);
            var version = entity.FindProperty("RowVersion")!;
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
            Assert.Contains(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType.Name == "Tenant");
            Assert.All(entity.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType.Name != "Tenant" &&
                fk.PrincipalEntityType.ClrType.Name != "ApplicationUser"), fk =>
                Assert.Equal(new[] { "TenantId", "Id" }, fk.PrincipalKey.Properties.Select(p => p.Name)));
        }
        AssertUnique(db, typeof(AssetBook), "TenantId", "AssetId");
        AssertUnique(db, typeof(AssetDepreciationRun), "TenantId", "ClientRequestId");
        AssertUnique(db, typeof(AssetDepreciationLine), "TenantId", "AssetBookId", "PeriodEnd");
        AssertUnique(db, typeof(AssetImpairment), "TenantId", "ClientRequestId");
        AssertUnique(db, typeof(AssetDisposal), "TenantId", "AssetBookId");
    }

    private static void AssertUnique(EduOSDbContext db, Type type, params string[] columns)
    {
        var index = Assert.Single(db.Model.FindEntityType(type)!.GetIndexes(), x =>
            x.Properties.Select(p => p.Name).SequenceEqual(columns));
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    [Fact]
    public async Task Sql_server_has_all_five_additive_tables_with_tenant_and_rowversion()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options);
        var count = await db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(*) AS [Value]
FROM sys.tables AS t
JOIN sys.columns AS rv ON rv.object_id = t.object_id AND rv.name = N'RowVersion'
JOIN sys.types AS typ ON typ.user_type_id = rv.user_type_id AND typ.name = N'timestamp'
WHERE t.name IN (N'AssetBooks', N'AssetDepreciationRuns', N'AssetDepreciationLines', N'AssetImpairments', N'AssetDisposals')
  AND EXISTS (SELECT 1 FROM sys.columns AS tenant WHERE tenant.object_id = t.object_id AND tenant.name = N'TenantId')").SingleAsync();
        Assert.Equal(5, count);
        var uniqueCount = await db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(*) AS [Value] FROM sys.indexes AS i
WHERE i.is_unique = 1 AND i.has_filter = 1 AND i.filter_definition LIKE N'%IsDeleted%'
  AND ((i.object_id = OBJECT_ID(N'dbo.AssetBooks') AND i.name = N'IX_AssetBooks_TenantId_AssetId')
    OR (i.object_id = OBJECT_ID(N'dbo.AssetDepreciationRuns') AND i.name = N'IX_AssetDepreciationRuns_TenantId_ClientRequestId')
    OR (i.object_id = OBJECT_ID(N'dbo.AssetDepreciationLines') AND i.name = N'IX_AssetDepreciationLines_TenantId_AssetBookId_PeriodEnd')
    OR (i.object_id = OBJECT_ID(N'dbo.AssetImpairments') AND i.name = N'IX_AssetImpairments_TenantId_ClientRequestId')
    OR (i.object_id = OBJECT_ID(N'dbo.AssetDisposals') AND i.name = N'IX_AssetDisposals_TenantId_AssetBookId'))").SingleAsync();
        Assert.Equal(5, uniqueCount);
    }
    [Fact]
    public async Task Sql_server_prevents_parallel_books_for_one_asset_and_stale_policy_updates()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, campusId, assetId;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-BOOK-" + suffix, Name = "Asset accounting concurrency CI",
                Email = "asset-" + suffix + "@example.test", State = TenantState.Active
            };
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus
                {
                    TenantId = tenantId, Code = "BOOK-" + suffix,
                    Name = "Asset book campus", IsActive = true
                };
                var category = new AssetCategory
                {
                    TenantId = tenantId, Code = "CAT-" + suffix,
                    Name = "Asset accounting CI", IsActive = true
                };
                setup.AddRange(campus, category);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var asset = new Asset
                {
                    TenantId = tenantId, CampusId = campusId, AssetCategoryId = category.Id,
                    AssetTag = "BOOK-" + suffix, Name = "Asset book concurrency",
                    PurchaseCost = 1000m, IsActive = true
                };
                setup.Assets.Add(asset);
                await setup.SaveChangesAsync();
                assetId = asset.Id;
            }
        }

        EduOSDbContext Scoped()
        {
            var http = new DefaultHttpContext();
            http.Items["TenantId"] = tenantId;
            return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        }

        async Task<bool> TryCreateAsync()
        {
            await using var db = Scoped();
            db.AssetBooks.Add(new AssetBook
            {
                TenantId = tenantId, CampusId = campusId, AssetId = assetId,
                InServiceDate = new DateOnly(2026, 1, 1), MethodCode = "StraightLine",
                UsefulLifeMonths = 24, CostBasis = 1000m, SalvageValue = 100m,
                PolicyVersion = 1, IsActive = true
            });
            await Task.Yield();
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryCreateAsync(), TryCreateAsync());
        Assert.Equal(1, results.Count(x => x));

        await using var first = Scoped();
        await using var second = Scoped();
        var firstBook = await first.AssetBooks.SingleAsync(x => x.AssetId == assetId);
        var secondBook = await second.AssetBooks.SingleAsync(x => x.AssetId == assetId);
        Assert.Equal(8, firstBook.RowVersion.Length);
        Assert.Equal(firstBook.RowVersion, secondBook.RowVersion);
        firstBook.UsefulLifeMonths = 36;
        await first.SaveChangesAsync();
        secondBook.PolicyVersion = 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = Scoped();
        var persisted = await verify.AssetBooks.AsNoTracking().SingleAsync(x => x.AssetId == assetId);
        Assert.Equal(36, persisted.UsefulLifeMonths);
        Assert.Equal(1, persisted.PolicyVersion);
    }

}
