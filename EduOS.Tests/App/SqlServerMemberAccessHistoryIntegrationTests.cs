using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server verification of the legacy permission-audit subquery and
/// cross-institution member-history read/export boundaries.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerMemberAccessHistoryIntegrationTests
{
    [Fact]
    public async Task Permission_history_uses_sql_tenant_and_member_scope_including_deleted_legacy_rows()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantAId, tenantBId, memberAId, memberBId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var setup = new EduOSDbContext(options))
        {
            var tenantA = new Tenant { Code = "CI-HISTA-" + suffix, Name = "History A",
                Email = "history-a-" + suffix + "@example.test", State = TenantState.Active };
            var tenantB = new Tenant { Code = "CI-HISTB-" + suffix, Name = "History B",
                Email = "history-b-" + suffix + "@example.test", State = TenantState.Active };
            var sharedName = "history-shared-" + suffix + "@example.test";
            var otherName = "history-other-" + suffix + "@example.test";
            var shared = new ApplicationUser { UserName = sharedName, NormalizedUserName = sharedName.ToUpperInvariant(),
                Email = sharedName, NormalizedEmail = sharedName.ToUpperInvariant(), FullName = "Shared member",
                IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            var other = new ApplicationUser { UserName = otherName, NormalizedUserName = otherName.ToUpperInvariant(),
                Email = otherName, NormalizedEmail = otherName.ToUpperInvariant(), FullName = "Other member",
                IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString() };
            var permission = new Permission { Code = "History.Test." + suffix, Name = "History test",
                ModuleCode = "Platform", IsActive = true };
            setup.Tenants.AddRange(tenantA, tenantB);
            setup.Users.AddRange(shared, other);
            setup.Permissions.Add(permission);
            await setup.SaveChangesAsync();
            tenantAId = tenantA.Id; tenantBId = tenantB.Id;
            setup.ChangeTracker.Clear();

            long legacyAId, legacyBId, otherPermissionId, otherMemberId;
            using (setup.BeginSystemTenantScope(tenantAId))
            {
                var memberA = new TenantMembership { TenantId = tenantAId, UserId = shared.Id,
                    Status = MembershipStatus.Active };
                var otherMember = new TenantMembership { TenantId = tenantAId, UserId = other.Id,
                    Status = MembershipStatus.Active };
                var legacyA = new UserPermission { TenantId = tenantAId, UserId = shared.Id,
                    PermissionId = permission.Id, IsAllowed = true };
                var otherPermission = new UserPermission { TenantId = tenantAId, UserId = other.Id,
                    PermissionId = permission.Id, IsAllowed = true };
                setup.TenantMemberships.AddRange(memberA, otherMember);
                setup.UserPermissions.AddRange(legacyA, otherPermission);
                await setup.SaveChangesAsync();
                memberAId = memberA.Id; otherMemberId = otherMember.Id;
                legacyAId = legacyA.Id; otherPermissionId = otherPermission.Id;
                legacyA.IsDeleted = true; // Historical permission row remains queryable only through audited legacy scope.
                await setup.SaveChangesAsync();
            }
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantBId))
            {
                var memberB = new TenantMembership { TenantId = tenantBId, UserId = shared.Id,
                    Status = MembershipStatus.Active };
                var legacyB = new UserPermission { TenantId = tenantBId, UserId = shared.Id,
                    PermissionId = permission.Id, IsAllowed = true };
                setup.TenantMemberships.Add(memberB);
                setup.UserPermissions.Add(legacyB);
                await setup.SaveChangesAsync();
                memberBId = memberB.Id; legacyBId = legacyB.Id;
            }
            setup.ChangeTracker.Clear();
            var now = DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
            setup.AuditLogs.AddRange(
                new AuditLog { TenantId = tenantAId, EntityName = nameof(TenantMembership), EntityId = memberAId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Current A",
                    UserName = "=HYPERLINK(1)", OccurredAt = now },
                new AuditLog { TenantId = tenantAId, EntityName = nameof(UserPermission), EntityId = legacyAId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Legacy A",
                    OccurredAt = now.AddDays(-40) },
                new AuditLog { TenantId = tenantAId, EntityName = nameof(UserPermission), EntityId = otherPermissionId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Other member A", OccurredAt = now },
                new AuditLog { TenantId = tenantAId, EntityName = nameof(TenantMembership), EntityId = otherMemberId,
                    Action = "TenantMemberRolesChanged", NewValue = "Other role A", OccurredAt = now },
                new AuditLog { TenantId = tenantBId, EntityName = nameof(UserPermission), EntityId = legacyBId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Foreign B", OccurredAt = now },
                new AuditLog { TenantId = tenantBId, EntityName = nameof(TenantMembership), EntityId = memberBId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Foreign current B", OccurredAt = now },
                new AuditLog { TenantId = tenantAId, EntityName = nameof(TenantMembership), EntityId = memberAId,
                    Action = "TenantMemberPermissionOverrideChanged", NewValue = "Failed A",
                    IsSuccess = false, OccurredAt = now });
            await setup.SaveChangesAsync();
        }

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantAId;
        await using var db = new EduOSDbContext(options, accessor);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.IsTenantAdmin).Returns(true);
        current.SetupGet(x => x.TenantId).Returns(tenantAId);
        current.SetupGet(x => x.UserId).Returns(999L);
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        var service = new TenantUserAdministrationService(db, current.Object, manager.Object,
            new Mock<IEmailService>().Object,
            Options.Create(new TenantPortalSettings { BaseDomain = "portal.example.test" }));

        var all = await service.GetMemberAccessHistoryAsync(memberAId, 1, 20, change: "permissions");
        Assert.True(all.Success, all.Message);
        Assert.Equal(2, all.Data!.TotalCount);
        Assert.Contains(all.Data.Items, x => x.NewValue == "Legacy A");
        Assert.Contains(all.Data.Items, x => x.NewValue == "Current A");

        var todayOnly = await service.GetMemberAccessHistoryAsync(memberAId, 1, 20,
            change: "permissions", from: today, to: today);
        Assert.True(todayOnly.Success, todayOnly.Message);
        Assert.Equal("Current A", Assert.Single(todayOnly.Data!.Items).NewValue);
        var csv = await service.ExportMemberAccessHistoryCsvAsync(memberAId,
            change: "permissions", from: today, to: today);
        Assert.True(csv.Success, csv.Message);
        Assert.Contains("Current A", csv.Data);
        Assert.Contains("'=HYPERLINK", csv.Data);
        Assert.DoesNotContain("Legacy A", csv.Data);
        Assert.DoesNotContain("Foreign B", csv.Data);
        Assert.DoesNotContain("Other member A", csv.Data);
        Assert.Equal(404, (await service.GetMemberAccessHistoryAsync(memberBId, 1, 20)).StatusCode);
        Assert.Equal(404, (await service.ExportMemberAccessHistoryCsvAsync(memberBId)).StatusCode);
        Assert.Equal(400, (await service.GetMemberAccessHistoryAsync(memberAId, 1, 20,
            from: today.AddDays(-400), to: today)).StatusCode);
        current.SetupGet(x => x.IsTenantAdmin).Returns(false);
        Assert.Equal(403, (await service.GetMemberAccessHistoryAsync(memberAId, 1, 20)).StatusCode);
        Assert.Equal(403, (await service.ExportMemberAccessHistoryCsvAsync(memberAId)).StatusCode);
    }
}
