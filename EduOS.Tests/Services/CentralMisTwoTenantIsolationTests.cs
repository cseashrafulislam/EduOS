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


    [Fact]
    public async Task Foreign_tenant_admin_role_without_membership_cannot_read_mis()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        // A role grant in the destination tenant must not substitute for
        // an active membership in that tenant.
        await using (var seed = Open(options, 42))
        {
            seed.Roles.Add(new ApplicationRole { Id = 4, TenantId = 42,
                Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true });
            seed.UserRoles.Add(new IdentityUserRole<long> { UserId = 9, RoleId = 4 });
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var foreign = Open(options, 42);
        var result = await Service(foreign, 42, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(403, result.StatusCode);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task External_membership_revocation_invalidates_subsequent_mis_request()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(200, (await service.GetAsync(From, To)).StatusCode);

        // Revoke from a separate context, as happens during an administrator
        // session; the next read must not trust an earlier successful request.
        await using (var administrator = Open(options, 41))
        {
            var membership = await administrator.TenantMemberships.SingleAsync(
                m => m.UserId == 9 && m.TenantId == 41);
            membership.Status = MembershipStatus.Suspended;
            await administrator.SaveChangesAsync();
        }

        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Negative_open_receivable_fails_closed_without_querying_finance_or_exposing_foreign_invoice()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            seed.StudentInvoices.Add(Invoice(1, 41, -0.01m));
            await seed.SaveChangesAsync();
        }
        await using (var foreignSeed = Open(options, 42))
        {
            SeedAdmin(foreignSeed, 42, 10, 3, 4);
            foreignSeed.StudentInvoices.Add(Invoice(2, 42, 999m));
            await foreignSeed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var tenantA = Open(options, 41);
        var invalid = await Service(tenantA, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(409, invalid.StatusCode);
        Assert.Null(invalid.Data);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // A malformed invoice in another tenant must not contaminate this
        // tenant's canonical operational receivable or GL-backed MIS metrics.
        await using (var repair = Open(options, 41))
        {
            var invoice = await repair.StudentInvoices.SingleAsync(x => x.Id == 1);
            invoice.DueAmount = 5m;
            await repair.SaveChangesAsync();
        }
        await using var valid = Open(options, 41);
        var accepted = await Service(valid, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, accepted.StatusCode);
        var metrics = accepted.Data!.Metrics.ToDictionary(x => x.Key);
        Assert.Equal(5m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoked_tenant_admin_role_denies_next_request_without_finance_access()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(200, (await service.GetAsync(From, To)).StatusCode);

        // An administrator revokes the role in another context while the
        // caller still presents the original TenantAdmin claims.
        await using (var administrator = Open(options, 41))
        {
            var role = await administrator.Roles.SingleAsync(r => r.Id == 2 && r.TenantId == 41);
            role.IsActive = false;
            await administrator.SaveChangesAsync();
        }

        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Foreign_tenant_negative_receivable_cannot_poison_valid_tenant_mis()
    {
        var options = Options();
        await using (var own = Open(options, 41))
        {
            SeedAdmin(own, 41, 9, 1, 2);
            own.StudentInvoices.Add(Invoice(1, 41, 12.50m));
            await own.SaveChangesAsync();
        }

        // A historical invalid row in another tenant must never cause an
        // authorized read to fail or affect the canonical receivable total.
        await using (var foreign = Open(options, 42))
        {
            SeedAdmin(foreign, 42, 10, 3, 4);
            foreign.StudentInvoices.Add(Invoice(2, 42, -999m));
            await foreign.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        var metrics = result.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(12.50m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalid_mis_period_rejects_before_finance_and_module_access()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(400, (await service.GetAsync(To, From)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(default, To)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 2))).StatusCode);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Finance_entitlement_disabled_hides_receivables_and_gl_even_with_invalid_invoice()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            seed.Students.Add(new Student { Id = 1, TenantId = 41,
                StudentCode = "A", FullName = "Allowed student", StatusCode = "Active" });
            seed.StudentInvoices.Add(Invoice(1, 41, -25m));
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var response = await Service(request, 41, 9, finance.Object, financeAvailable: false)
            .GetAsync(From, To);
        Assert.Equal(200, response.StatusCode);
        var metrics = response.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(1m, metrics["active_students"].Value);
        foreach (var key in new[] { "outstanding_fee_amount", "outstanding_fee_invoices",
                     "posted_income", "posted_expenses", "net_surplus" })
        {
            Assert.False(metrics[key].Available);
            Assert.Null(metrics[key].Value);
        }
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Foreign_currency_open_invoice_fails_closed_before_gl_reporting()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            var invoice = Invoice(1, 41, 150m);
            invoice.CurrencyCode = "USD";
            seed.StudentInvoices.Add(invoice);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(409, result.StatusCode);
        Assert.Null(result.Data);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Closed_invoice_stale_negative_due_does_not_pollute_open_receivables()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            var closed = Invoice(1, 41, -100m);
            closed.State = InvoiceState.Cancelled;
            seed.StudentInvoices.Add(closed);
            seed.StudentInvoices.Add(Invoice(2, 41, 25m));
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, result.StatusCode);
        var metrics = result.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(25m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Partially_paid_invoice_counts_only_remaining_due_not_original_total()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            var partial = Invoice(1, 41, 30m);
            partial.State = InvoiceState.PartiallyPaid;
            partial.TotalAmount = 100m;
            seed.StudentInvoices.Add(partial);
            seed.StudentInvoices.Add(Invoice(2, 41, 0m));
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, result.StatusCode);
        var metrics = result.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(30m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleted_and_paid_invoices_with_stale_due_are_excluded_from_receivable()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            var deleted = Invoice(1, 41, -50m);
            deleted.IsDeleted = true;
            var paid = Invoice(2, 41, -60m);
            paid.State = InvoiceState.Paid;
            seed.StudentInvoices.AddRange(deleted, paid, Invoice(3, 41, 7m));
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, result.StatusCode);
        var metrics = result.Data!.Metrics.ToDictionary(m => m.Key);
        Assert.Equal(7m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(f => f.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Membership_reactivation_requires_live_active_role_before_mis_can_resume()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(200, (await service.GetAsync(From, To)).StatusCode);

        await using (var admin = Open(options, 41))
        {
            var membership = await admin.TenantMemberships.SingleAsync(
                x => x.TenantId == 41 && x.UserId == 9);
            membership.Status = MembershipStatus.Suspended;
            var role = await admin.Roles.SingleAsync(x => x.TenantId == 41 && x.Id == 2);
            role.IsActive = false;
            await admin.SaveChangesAsync();
        }

        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        await using (var admin = Open(options, 41))
        {
            var membership = await admin.TenantMemberships.SingleAsync(
                x => x.TenantId == 41 && x.UserId == 9);
            membership.Status = MembershipStatus.Active;
            await admin.SaveChangesAsync();
        }

        // Restoring membership must not resurrect an independently revoked role.
        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Role_reactivation_cannot_bypass_still_suspended_membership()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            var membership = await seed.TenantMemberships.SingleAsync(
                x => x.TenantId == 41 && x.UserId == 9);
            membership.Status = MembershipStatus.Suspended;
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);

        await using (var admin = Open(options, 41))
        {
            var role = await admin.Roles.SingleAsync(x => x.TenantId == 41 && x.Id == 2);
            role.IsActive = false;
            await admin.SaveChangesAsync();
            role.IsActive = true;
            await admin.SaveChangesAsync();
        }

        Assert.Equal(403, (await service.GetAsync(From, To)).StatusCode);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }


    [Fact]
    public async Task Removing_user_role_grant_revokes_mis_access_without_disabling_role()
    {
        var options = Options();
        await using (var seed = Open(options, 41))
        {
            SeedAdmin(seed, 41, 9, 1, 2);
            await seed.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var service = Service(request, 41, 9, finance.Object);
        Assert.Equal(200, (await service.GetAsync(From, To)).StatusCode);

        // A revoked user-role join is distinct from a disabled role or membership.
        // The next request must re-evaluate the live grant, not cached claims.
        await using (var administrator = Open(options, 41))
        {
            var grant = await administrator.UserRoles.SingleAsync(
                x => x.UserId == 9 && x.RoleId == 2);
            administrator.UserRoles.Remove(grant);
            await administrator.SaveChangesAsync();
        }

        var denied = await service.GetAsync(From, To);
        Assert.Equal(403, denied.StatusCode);
        Assert.Null(denied.Data);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Foreign_tenant_invalid_currency_cannot_block_authorized_receivable()
    {
        var options = Options();
        await using (var own = Open(options, 41))
        {
            SeedAdmin(own, 41, 9, 1, 2);
            own.StudentInvoices.Add(Invoice(1, 41, 8.75m));
            await own.SaveChangesAsync();
        }
        await using (var foreign = Open(options, 42))
        {
            SeedAdmin(foreign, 42, 10, 3, 4);
            var invoice = Invoice(2, 42, 100m);
            invoice.CurrencyCode = "USD";
            foreign.StudentInvoices.Add(invoice);
            await foreign.SaveChangesAsync();
        }

        var finance = Finance();
        await using var request = Open(options, 41);
        var result = await Service(request, 41, 9, finance.Object).GetAsync(From, To);
        Assert.Equal(200, result.StatusCode);
        var metrics = result.Data!.Metrics.ToDictionary(x => x.Key);
        Assert.Equal(8.75m, metrics["outstanding_fee_amount"].Value);
        Assert.Equal(1m, metrics["outstanding_fee_invoices"].Value);
        finance.Verify(x => x.GetInstitutionalIncomeExpenseTotalsAsync(
            From, To, It.IsAny<CancellationToken>()), Times.Once);
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
        long userId, IAccountingReportingService finance, bool financeAvailable = true)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(m => m.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync((string module) => module != "FINANCE" || financeAvailable);
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
