using EduOS.Core.Entities.Accounting;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Common;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real-SQL acceptance for the central inbox UNION ALL, server-owned campus
/// visibility, live role revocation, maker exclusion and institution-wide GL.
/// Source workflows remain the only approval/posting authority.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerCentralWorkCenterTests
{
    [Fact]
    public async Task SQL_three_source_pending_queue_is_scoped_paged_and_read_only()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, userId;
        await using (var root = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CENT-W-" + suffix,
                Name = "Central work SQL", Email = "cw-" + suffix + "@example.test",
                State = TenantState.Active };
            root.Tenants.Add(tenant);
            await root.SaveChangesAsync();
            tenantId = tenant.Id;
            var reviewer = new ApplicationUser { UserName = "cw-" + suffix,
                NormalizedUserName = "CW-" + suffix.ToUpperInvariant() };
            var role = new ApplicationRole { TenantId = tenantId,
                Name = "TenantAdmin", NormalizedName = "TENANTADMIN" };
            root.Users.Add(reviewer);
            root.Roles.Add(role);
            await root.SaveChangesAsync();
            userId = reviewer.Id;
            root.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = role.Id });
            await root.SaveChangesAsync();
        }

        await using var db = Open(options, tenantId);
        var campus = new Campus { TenantId = tenantId,
            Code = "CW-C-" + suffix, Name = "Central campus", IsActive = true };
        var period = new AccountingPeriod { TenantId = tenantId,
            Name = "Inbox review " + suffix,
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 30) };
        db.AddRange(campus, period);
        db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId,
            UserId = userId, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();

        var reviewerOwned = new ProcurementRequisition { TenantId = tenantId,
            ClientRequestId = Guid.NewGuid(),
            CampusId = campus.Id, RequestNumber = "CW-OWN-" + suffix,
            Purpose = "Maker cannot approve own request", StateCode = "Submitted",
            CreatedBy = userId, SubmittedAt = new DateTime(2026, 11, 5, 7, 0, 0, DateTimeKind.Utc) };
        var pendingReq = new ProcurementRequisition { TenantId = tenantId,
            ClientRequestId = Guid.NewGuid(),
            CampusId = campus.Id, RequestNumber = "CW-R-" + suffix,
            Purpose = "Independent approval", StateCode = "Submitted",
            CreatedBy = userId + 100, SubmittedAt = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc) };
        var pendingPolicy = new FacilityVisitorPolicyChange { TenantId = tenantId,
            ClientRequestId = Guid.NewGuid(),
            CampusId = campus.Id, PolicyCode = "RetentionDays", ProposedValue = "90",
            RequestReason = "Review visitor retention", StateCode = "Submitted",
            RequestedByUserId = userId + 100,
            CreatedAt = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc) };
        var pendingJournal = new Journal { TenantId = tenantId,
            JournalNumber = "CW-J-" + suffix, AccountingPeriodId = period.Id,
            JournalDate = new DateOnly(2026, 11, 5), State = JournalState.Submitted,
            SourceType = "Manual", CreatedBy = userId + 100,
            CreatedAt = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc) };
        db.AddRange(reviewerOwned, pendingReq, pendingPolicy, pendingJournal);
        await db.SaveChangesAsync();

        // The normal audit interceptor may attribute test inserts to the local
        // actor. Restore deterministic independent makers without posting.
        await db.ProcurementRequisitions.Where(x => x.Id == pendingReq.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, userId + 100));
        await db.ProcurementRequisitions.Where(x => x.Id == reviewerOwned.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, userId));
        await db.Journals.Where(x => x.Id == pendingJournal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedBy, userId + 100));
        db.ChangeTracker.Clear();

        var moduleMock = new Mock<ITenantModuleService>();
        foreach (var code in new[] { "INVENTORY", "FACILITIES", "FINANCE" })
            moduleMock.Setup(x => x.IsCurrentTenantModuleAvailableAsync(code)).ReturnsAsync(true);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PilotRelease:Enabled"] = "false" }).Build();
        var service = new CentralWorkCenterService(db,
            new User(tenantId, userId), moduleMock.Object, config);

        var first = await service.GetPendingAsync(page: 1, pageSize: 2);
        var second = await service.GetPendingAsync(page: 2, pageSize: 2);
        Assert.Equal(200, first.StatusCode);
        Assert.Equal(200, second.StatusCode);
        Assert.Equal(3, first.Data!.Pending.TotalCount);
        Assert.Equal(3, second.Data!.Pending.TotalCount);
        var items = first.Data.Pending.Items.Concat(second.Data.Pending.Items).ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal(3, items.Select(x => x.Source).Distinct().Count());
        Assert.DoesNotContain(items, x => x.SourceId == reviewerOwned.Id &&
            x.Source == "ProcurementRequisition");
        Assert.Contains(items, x => x.Source == "AccountingJournal" &&
            x.SourceReference == pendingJournal.PublicId && x.CampusId == null);
        Assert.False(db.ChangeTracker.HasChanges());

        var campusOnly = await service.GetPendingAsync(campusId: campus.Id);
        Assert.Equal(200, campusOnly.StatusCode);
        Assert.Equal(2, campusOnly.Data!.Pending.TotalCount);
        Assert.False(campusOnly.Data.FinanceIncluded);
        Assert.DoesNotContain(campusOnly.Data.Pending.Items,
            x => x.Source == "AccountingJournal");
        Assert.Equal(403, (await service.GetPendingAsync(campusId: campus.Id + 100)).StatusCode);

        // The institution-wide MIS shares the same live membership/role boundary
        // and queries canonical domain tables on SQL Server rather than snapshots.
        var financeMock = new Mock<IAccountingReportingService>();
        financeMock.Setup(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<InstitutionalIncomeExpenseTotalsDto>.SuccessResponse(
                new InstitutionalIncomeExpenseTotalsDto { TotalIncome = 120m,
                    TotalExpenses = 20m, NetSurplus = 100m }));
        var mis = new CentralInstitutionMisService(db, new User(tenantId, userId),
            moduleMock.Object, financeMock.Object, TimeProvider.System);
        var misResult = await mis.GetAsync(new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30));
        Assert.Equal(200, misResult.StatusCode);
        Assert.Equal(2m, Assert.Single(misResult.Data!.Metrics,
            m => m.Key == "submitted_requisitions").Value);
        Assert.Equal(100m, Assert.Single(misResult.Data.Metrics,
            m => m.Key == "net_surplus").Value);

        // Direct-service SQL acceptance, independent of middleware. A stale
        // TenantAdmin cookie must not restore access after membership or role
        // revocation, and a switched ambient tenant must never leak the inbox
        // or aggregate institution-wide finance/operations metrics.
        await using (var switched = Open(options, tenantId + 100000))
        {
            var switchedInbox = new CentralWorkCenterService(switched,
                new User(tenantId, userId), moduleMock.Object, config);
            var switchedMis = new CentralInstitutionMisService(switched,
                new User(tenantId, userId), moduleMock.Object, financeMock.Object,
                TimeProvider.System);
            Assert.Equal(403, (await switchedInbox.GetPendingAsync()).StatusCode);
            Assert.Equal(403, (await switchedMis.GetAsync(new DateOnly(2026, 11, 1),
                new DateOnly(2026, 11, 30))).StatusCode);
        }

        await db.TenantMemberships.Where(x => x.TenantId == tenantId && x.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MembershipStatus.Suspended));
        Assert.Equal(403, (await service.GetPendingAsync()).StatusCode);
        Assert.Equal(403, (await mis.GetAsync(new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30))).StatusCode);
        await db.Roles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.NormalizedName == "TENANTADMIN")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await db.Roles.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.NormalizedName == "TENANTADMIN")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        Assert.Equal(403, (await service.GetPendingAsync()).StatusCode);
        Assert.Equal(403, (await mis.GetAsync(new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30))).StatusCode);
        var restoredMembershipRows = await db.TenantMemberships.Where(x =>
                x.TenantId == tenantId && x.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MembershipStatus.Active));
        Assert.Equal(1, restoredMembershipRows);
        Assert.True(await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.Status == MembershipStatus.Active));
        Assert.True(await db.Roles.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.NormalizedName == "TENANTADMIN" && x.IsActive));
        var resumedInbox = await service.GetPendingAsync();
        Assert.True(resumedInbox.StatusCode == 200,
            $"Expected restored inbox access; got {resumedInbox.StatusCode}: {resumedInbox.Message}");
        Assert.Equal(200, (await mis.GetAsync(new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30))).StatusCode);

        await db.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        Assert.Equal(403, (await service.GetPendingAsync()).StatusCode);
        Assert.Equal(403, (await mis.GetAsync(new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30))).StatusCode);
    }

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options,
            new FixedHttpContextAccessor { HttpContext = http });
    }

    // HttpContextAccessor uses a shared AsyncLocal: opening a deliberately
    // switched tenant context would otherwise replace the first context's
    // tenant mid-test and turn later role-revocation assertions into false
    // positives. Each simulated request needs its own immutable context.
    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class User(long tenant, long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
