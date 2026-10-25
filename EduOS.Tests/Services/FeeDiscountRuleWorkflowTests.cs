using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeDiscountRuleWorkflowTests
{
    [Fact]
    public async Task Tenant_admin_can_create_and_replay_a_matching_rule_without_duplicates()
    {
        await using var db = Database();
        var service = Service(db, new User("TenantAdmin"));
        var request = new SaveDiscountRuleRequestDto
        {
            Name = "Sibling tuition", IsPercentage = true, Value = 12.5000m,
            EffectiveFrom = new DateOnly(2026, 11, 1), IsActive = true
        };
        var first = await service.SaveDiscountRuleAsync(null, request);
        first.StatusCode.Should().Be(200);
        var replay = await service.SaveDiscountRuleAsync(null, request);
        replay.StatusCode.Should().Be(200);
        replay.Data!.Id.Should().Be(first.Data!.Id);
        db.DiscountRules.Count().Should().Be(1);
    }

    [Fact]
    public async Task Same_name_with_different_financial_terms_is_rejected()
    {
        await using var db = Database();
        var service = Service(db, new User("TenantAdmin"));
        await service.SaveDiscountRuleAsync(null, new SaveDiscountRuleRequestDto
            { Name = "Merit", IsPercentage = true, Value = 10m });
        var conflict = await service.SaveDiscountRuleAsync(null, new SaveDiscountRuleRequestDto
            { Name = "Merit", IsPercentage = true, Value = 20m });
        conflict.StatusCode.Should().Be(409);
        db.DiscountRules.Count().Should().Be(1);
    }

    [Fact]
    public async Task Non_admin_cannot_read_or_write_tenant_wide_discount_rules()
    {
        await using var db = Database();
        var service = Service(db, new User("Accountant"));
        (await service.GetDiscountRulesAsync()).StatusCode.Should().Be(403);
        (await service.SaveDiscountRuleAsync(null, new SaveDiscountRuleRequestDto
            { Name = "Unauthorized", IsPercentage = true, Value = 10m })).StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(101.0)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(10.12345)]
    public async Task Invalid_percentage_is_rejected_without_writing(double value)
    {
        await using var db = Database();
        var service = Service(db, new User("TenantAdmin"));
        var result = await service.SaveDiscountRuleAsync(null, new SaveDiscountRuleRequestDto
            { Name = "Invalid", IsPercentage = true, Value = (decimal)value });
        result.StatusCode.Should().Be(400);
        db.DiscountRules.Count().Should().Be(0);
    }

    [Fact]
    public async Task Paged_register_excludes_other_tenants_and_validates_page_size()
    {
        var databaseName = "discount-rules-" + Guid.NewGuid().ToString("N");
        await using (var foreign = Database(databaseName, 42))
        {
            foreign.DiscountRules.Add(new DiscountRule { TenantId = 42, Name = "Foreign",
                IsPercentage = true, Value = 15m });
            await foreign.SaveChangesAsync();
        }
        await using var db = Database(databaseName, 41);
        var service = Service(db, new User("TenantAdmin"));
        (await service.GetDiscountRulesAsync(1, 101)).StatusCode.Should().Be(400);
        var result = await service.GetDiscountRulesAsync();
        result.StatusCode.Should().Be(200);
        result.Data!.TotalCount.Should().Be(0);
    }

    private static FeeBillingService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<FeeStructure>(db), new GenericRepository<FeeStructureLine>(db),
        new GenericRepository<FeeHead>(db), new GenericRepository<StudentDiscount>(db),
        new GenericRepository<DiscountRule>(db), new GenericRepository<StudentInvoice>(db),
        new GenericRepository<StudentInvoiceLine>(db), new GenericRepository<StudentPayment>(db),
        new GenericRepository<PaymentAllocation>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Campus>(db), new GenericRepository<Tenant>(db),
        db, user, TimeProvider.System, NullLogger<FeeBillingService>.Instance);

    private static EduOSDbContext Database(string? name = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "discount-rules-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
