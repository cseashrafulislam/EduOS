using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class PlatformDashboardServiceTests
{
    [Fact]
    public async Task Platform_dashboard_denies_non_superadmin()
    {
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var result = await Create(db, new CurrentUser(false)).GetPlatformDashboardAsync();
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Platform_dashboard_counts_tenants_without_a_tenant_context()
    {
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Tenants.Add(new Tenant { Name = "Example School", Code = "PLATFORM-01",
            Email = "school@example.test", State = EduOS.Core.Enums.Domain.TenantState.Active });
        await db.SaveChangesAsync();
        var result = await Create(db, new CurrentUser(true)).GetPlatformDashboardAsync();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.TotalTenants.Should().Be(1);
        result.Data.ActiveTenants.Should().Be(1);
    }
    private static DashboardService Create(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Tenant>(db), new GenericRepository<InstitutionTypeDefinition>(db),
        new GenericRepository<SubscriptionPlan>(db), new GenericRepository<PlanFeature>(db),
        new TenantSubscriptionRepository(db), new GenericRepository<Student>(db),
        new GenericRepository<Employee>(db), new GenericRepository<Campus>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<StudentInvoice>(db), user, NullLogger<DashboardService>.Instance);
    private sealed class CurrentUser(bool isAdmin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 1;
        public long TenantId => 0;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => isAdmin;
        public bool IsTenantAdmin => !isAdmin;
        public IReadOnlyList<string> Roles => isAdmin ? ["SuperAdmin"] : ["TenantAdmin"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
