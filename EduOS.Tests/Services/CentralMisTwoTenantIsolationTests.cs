using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

/// <summary>
/// Synthetic shared-store MIS isolation and live membership regression.
/// InMemory tests are NOT a substitute for SQL Server RLS/query-plan or cookie tests.
/// </summary>
public sealed class CentralMisTwoTenantIsolationTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task Same_store_foreign_students_and_receivables_do_not_leak_into_authorized_mis()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            seed.Students.Add(new Student { Id = 1, TenantId = 41, StudentCode = "A",
                FullName = "Tenant A", StatusCode = "Active" });
            seed.StudentInvoices.Add(Invoice(1, 41, 10m));
            await seed.SaveChangesAsync();
        }
        // The two tenants share one InMemory store but never one request-bound
        // DbContext. Production tenant scope explicitly prohibits switching a
        // context after binding it to an institution.
        await using (var seed = Open(options, 42))
        {
            SeedAdmin(seed, 42, 10, 3, 4);
            seed.Students.Add(new Student { Id = 2, TenantId = 42, StudentCode = "B",
                FullName = "Tenant B", StatusCode = "Active" });
            seed.Students.Add(new Student { Id = 3, TenantId = 42, StudentCode = "C",
                FullName = "Tenant B second", StatusCode = "Active" });
            seed.StudentInvoices.Add(Invoice(2, 42, 900m));
            await seed.SaveChangesAsync();
        }

        await using var tenantA = Open(options, 41);
        var financeA = Finance();
        var resultA = await Service(tenantA, 41, 9, financeA.Object).GetAsync(From, To);
        Assert.Equal(200, resultA.StatusCode);
        var metricsA = resultA.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(1m, metricsA["active_students"].Value);
        Assert.Equal(10m, metricsA["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metricsA["outstanding_fee_invoices"].Value);
        financeA.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(From, To,
            It.IsAny<CancellationToken>()), Times.Once);

        await using var tenantB = Open(options, 42);
        var resultB = await Service(tenantB, 42, 10, Finance().Object).GetAsync(From, To);
        Assert.Equal(200, resultB.StatusCode);
        var metricsB = resultB.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(2m, metricsB["active_students"].Value);
        Assert.Equal(900m, metricsB["outstanding_fee_amount"].Value);
    }

    [Fact]
    public async Task Forged_tenant_context_and_revoked_membership_fail_before_finance_is_queried()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using (var wrongContext = Open(options, 42))
        {
            Assert.Equal(403, (await Service(wrongContext, 41, 9, finance.Object)
                .GetAsync(From, To)).StatusCode);
        }

        await using (var db = Open(options, 41))
        {
            var service = Service(db, 41, 9, finance.Object);
            Assert.Equal(200, (await service.GetAsync(From, To)).StatusCode);
            var membership = await db.TenantMemberships.SingleAsync(m => m.UserId == 9);
            membership.Status = MembershipStatus.Suspended;
            await db.SaveChangesAsync();
            Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        }
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("mis-two-tenant-" + Guid.NewGuid().ToString("N"),
                new InMemoryDatabaseRoot()).Options;

    private static EduOSDbContext Open(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static void SeedAdmin(EduOSDbContext db, long tenant, long userId,
        long membershipId, long roleId)
    {
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "mis-" + userId });
        db.TenantMemberships.Add(new TenantMembership { Id = membershipId,
            TenantId = tenant, UserId = userId, Status = MembershipStatus.Active });
        db.Roles.Add(new ApplicationRole { Id = roleId, TenantId = tenant,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = roleId });
    }

    private static StudentInvoice Invoice(long id, long tenant, decimal due) => new()
    {
        Id = id, TenantId = tenant, StudentEnrollmentId = id,
        InvoiceNumber = "MIS-" + id, InvoiceDate = From, DueDate = To,
        State = InvoiceState.Issued, TotalAmount = due, DueAmount = due
    };

    private static Mock<IAccountingReportingService> Finance()
    {
        var finance = new Mock<IAccountingReportingService>();
        finance.Setup(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
                From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<InstitutionalIncomeExpenseTotalsDto>.SuccessResponse(
                new InstitutionalIncomeExpenseTotalsDto { FromDate = From, ToDate = To }));
        return finance;
    }

    private static CentralInstitutionMisService Service(EduOSDbContext db, long tenant,
        long userId, IAccountingReportingService finance)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(m => m.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        return new CentralInstitutionMisService(db, new Actor(tenant, userId),
            modules.Object, finance, TimeProvider.System);
    }

    private sealed class Actor(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
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
