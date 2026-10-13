using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FinanceAdjustmentServiceTests
{
    [Fact]
    public async Task Discount_rules_are_tenant_scoped()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-discounts-"+Guid.NewGuid().ToString("N")).Options,accessor);
        db.DiscountRules.AddRange(
            new DiscountRule{TenantId=11,Name="Own",Value=10m,IsPercentage=true},
            new DiscountRule{TenantId=12,Name="Foreign",Value=15m,IsPercentage=true});
        await db.SaveChangesAsync();
        var result=await new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System).GetDiscountRulesAsync();
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle().Which.Name.Should().Be("Own");
    }
    [Fact]
    public async Task Non_admin_cannot_access_tenant_finance_adjustments()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-security-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,false),TimeProvider.System);
        (await service.GetFineRulesAsync()).StatusCode.Should().Be(403);
        (await service.GetDiscountRulesAsync()).StatusCode.Should().Be(403);
        (await service.AssignDiscountAsync(new AssignStudentDiscountRequestDto())).StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Invalid_discount_precision_is_rejected_before_database_write()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-precision-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        var result=await service.SaveDiscountRuleAsync(null,new SaveDiscountRuleRequestDto
            {Name="Invalid precision",Value=0.00001m,IsPercentage=true});
        result.Success.Should().BeFalse();
        db.DiscountRules.Should().BeEmpty();
    }
    private sealed class TestUser(long tenant,bool admin):ICurrentUserService
    {
        public bool IsAuthenticated=>true;
        public long UserId=>99;
        public long TenantId=>tenant;
        public string? FullName=>"Test";
        public string? Email=>null;
        public bool IsSuperAdmin=>false;
        public bool IsTenantAdmin=>admin;
        public IReadOnlyList<string> Roles=>admin?["TenantAdmin"]:["Staff"];
        public bool IsInRole(string role)=>Roles.Contains(role);
        public string? IpAddress=>null;
        public string? UserAgent=>null;
    }
}
