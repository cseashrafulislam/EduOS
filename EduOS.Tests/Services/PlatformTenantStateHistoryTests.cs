using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PlatformTenantStateHistoryTests
{
    [Fact]
    public async Task Host_history_is_paged_scoped_to_canonical_tenant_and_excludes_other_audits()
    {
        var http = new DefaultHttpContext();
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("host-state-history-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        db.Tenants.AddRange(
            new Tenant { Id = 101, Name = "First", Code = "FIRST", Email = "first@example.test" },
            new Tenant { Id = 202, Name = "Second", Code = "SECOND", Email = "second@example.test" });
        var now = DateTime.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog { Id = 1, TenantId = 101, EntityName = "Tenant", EntityId = 101,
                Action = "StateChange", UserName = "Host admin", OldValue = "Active",
                NewValue = "{\"State\":\"Suspended\",\"Reason\":\"Verified policy violation\"}",
                RequestId = Guid.NewGuid().ToString("D"), OccurredAt = now.AddMinutes(-2) },
            new AuditLog { Id = 2, TenantId = 101, EntityName = "Tenant", EntityId = 101,
                Action = "StateChange", UserName = "Host admin", OldValue = "Suspended",
                NewValue = "{\"State\":\"Active\",\"Reason\":\"Review complete\"}",
                OccurredAt = now.AddMinutes(-1) },
            new AuditLog { Id = 3, TenantId = 202, EntityName = "Tenant", EntityId = 202,
                Action = "StateChange", NewValue = "{\"State\":\"Suspended\"}" },
            new AuditLog { Id = 4, TenantId = 101, EntityName = "Student", EntityId = 101,
                Action = "StateChange", NewValue = "not an institution transition" },
            new AuditLog { Id = 5, TenantId = 101, EntityName = "Tenant", EntityId = 101,
                Action = "StateChange", NewValue = "failed", IsSuccess = false });
        await db.SaveChangesAsync();
        var host = CreateService(db, true);
        var first = await host.GetStateHistoryAsync(101, 1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.Single(first.Data.Items);
        Assert.Equal("Active", first.Data.Items[0].TargetState);
        Assert.Equal("Review complete", first.Data.Items[0].Reason);
        var second = await host.GetStateHistoryAsync(101, 2, 1);
        Assert.True(second.Success);
        Assert.Equal("Suspended", second.Data!.Items[0].TargetState);
        Assert.Equal("Verified policy violation", second.Data.Items[0].Reason);
        Assert.Equal(404, (await host.GetStateHistoryAsync(303, 1, 20)).StatusCode);
        Assert.Equal(400, (await host.GetStateHistoryAsync(101, 0, 20)).StatusCode);
        Assert.Equal(403, (await CreateService(db, false).GetStateHistoryAsync(101, 1, 20)).StatusCode);
    }

    private static PlatformTenantLifecycleService CreateService(EduOSDbContext db, bool host) => new(
        new GenericRepository<Tenant>(db), new GenericRepository<TenantSubscription>(db),
        new GenericRepository<AuditLog>(db), db, new HostUser(host), TimeProvider.System,
        NullLogger<PlatformTenantLifecycleService>.Instance);

    private sealed class HostUser(bool host) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => 0;
        public string? FullName => "Host admin";
        public string? Email => "host@example.test";
        public bool IsTenantAdmin => false;
        public bool IsSuperAdmin => host;
        public IReadOnlyList<string> Roles => host ? ["SuperAdmin"] : [];
        public bool IsInRole(string role) => host && role == "SuperAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
