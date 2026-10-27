using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentLifecycleTimelinePagingTests
{
    [Fact]
    public async Task Mixed_status_promotion_and_exit_facts_are_paged_in_stable_historical_order()
    {
        await using var db = Context(71);
        var reference = Guid.NewGuid();
        db.Students.Add(new Student { Id = 10, TenantId = 71, PublicId = reference,
            StudentCode = "S10", FullName = "Former learner", StatusCode = "Passed",
            AdmissionDate = new DateOnly(2024, 1, 1) });
        var t = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        db.StudentStatusHistories.AddRange(
            new StudentStatusHistory { Id = 21, TenantId = 71, StudentId = 10,
                FromStatusCode = "Active", ToStatusCode = "Suspended", EffectiveAt = t.AddDays(1), ChangedByUserId = 7 },
            new StudentStatusHistory { Id = 22, TenantId = 71, StudentId = 10,
                FromStatusCode = "Suspended", ToStatusCode = "Active", EffectiveAt = t.AddDays(1), ChangedByUserId = 7 });
        db.StudentPromotionRecords.Add(new StudentPromotionRecord { Id = 30, TenantId = 71,
            StudentId = 10, FromEnrollmentId = 1, ToEnrollmentId = 2,
            Decision = StudentProgressionDecisionType.Promoted, ProcessedAt = t.AddDays(1), ProcessedByUserId = 7 });
        db.StudentExitRecords.Add(new StudentExitRecord { Id = 40, TenantId = 71,
            StudentId = 10, StudentEnrollmentId = 2, ExitType = StudentExitType.Completed,
            ProcessedAt = t, ProcessedByUserId = 7 });
        await db.SaveChangesAsync();

        var service = new StudentLifecycleTimelineService(db, new Actor(71));
        var first = await service.GetPageAsync(reference, 1, 2);
        Assert.True(first.Success, first.Message);
        Assert.True(first.Data!.HasMore);
        Assert.Equal(1, first.Data.Page);
        Assert.Equal(2, first.Data.PageSize);
        Assert.Equal(new[] { "Active", "Suspended" }, first.Data.Events.Select(x => x.ToStatus));
        var second = await service.GetPageAsync(reference, 2, 2);
        Assert.True(second.Success, second.Message);
        Assert.False(second.Data!.HasMore);
        Assert.Equal(new[] { "Progression", "Exit" }, second.Data.Events.Select(x => x.EventType));
        Assert.Equal("Promoted", second.Data.Events[0].Detail);
        Assert.Equal("Completed", second.Data.Events[1].Detail);
        Assert.Equal("Passed", second.Data.CurrentStatus);
    }

    [Fact]
    public async Task Invalid_pages_cross_tenant_and_unprivileged_access_are_denied()
    {
        await using var db = Context(71);
        var reference = Guid.NewGuid();
        db.Students.Add(new Student { Id = 10, TenantId = 71, PublicId = reference,
            StudentCode = "S10", FullName = "Learner", AdmissionDate = new DateOnly(2024, 1, 1) });
        await db.SaveChangesAsync();
        var service = new StudentLifecycleTimelineService(db, new Actor(71));
        Assert.Equal(400, (await service.GetPageAsync(Guid.Empty)).StatusCode);
        Assert.Equal(400, (await service.GetPageAsync(reference, 0)).StatusCode);
        Assert.Equal(400, (await service.GetPageAsync(reference, 51)).StatusCode);
        Assert.Equal(400, (await service.GetPageAsync(reference, 1, 101)).StatusCode);
        Assert.Equal(404, (await new StudentLifecycleTimelineService(db, new Actor(72))
            .GetPageAsync(reference)).StatusCode);
        Assert.Equal(403, (await new StudentLifecycleTimelineService(db, new Actor(71, false))
            .GetPageAsync(reference)).StatusCode);
    }

    [Fact]
    public void Endpoint_exposes_page_and_page_size_without_mutation()
    {
        var get = typeof(StudentLifecycleTimelineController).GetMethod(nameof(StudentLifecycleTimelineController.Get))!;
        Assert.NotNull(get.GetParameters().SingleOrDefault(x => x.Name == "page"));
        Assert.NotNull(get.GetParameters().SingleOrDefault(x => x.Name == "pageSize"));
        Assert.Single(get.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-lifecycle-paging-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class Actor(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? new[] { "TenantAdmin" } : new[] { "Teacher" };
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
