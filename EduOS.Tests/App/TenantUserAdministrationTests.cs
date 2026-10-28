using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.System;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantUserAdministrationTests
{
    [Fact]
    public async Task Roles_only_list_matching_institution_and_reserved_name_is_rejected()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        await SeedSystemRolesAsync(db, access.Accessor,
            new ApplicationRole { TenantId = 100, Name = "Teacher", NormalizedName = "TEACHER" },
            new ApplicationRole { TenantId = 200, Name = "Principal", NormalizedName = "PRINCIPAL" });
        var service = Service(db, access.User, access.Email);
        var roles = await service.GetRolesAsync();
        Assert.True(roles.Success);
        Assert.Single(roles.Data!);
        Assert.Equal("Teacher", roles.Data![0].Name);
        var forbidden = await service.CreateRoleAsync(new TenantRoleCreateRequestDto { Name = "SuperAdmin" });
        Assert.False(forbidden.Success);
        Assert.Equal(400, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_assign_permissions_to_another_tenants_role()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        await SeedSystemRolesAsync(db, access.Accessor, new ApplicationRole
        {
            Id = 222, TenantId = 200, Name = "Accountant",
            NormalizedName = "ACCOUNTANT", IsActive = true
        });
        var service = Service(db, access.User, access.Email);
        var result = await service.SetRolePermissionsAsync(222,
            new TenantRolePermissionsRequestDto { PermissionIds = Array.Empty<long>(), ConcurrencyStamp = "test-stamp" });
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(db.RolePermissions);
    }

    [Fact]
    public async Task Permission_save_rejects_stale_role_version_without_writing_grants()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Roles.Add(new ApplicationRole
        {
            Id = 333, TenantId = 100, Name = "Teacher",
            NormalizedName = "TEACHER", IsActive = true,
            ConcurrencyStamp = "current"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = Service(db, access.User, access.Email);
        var result = await service.SetRolePermissionsAsync(333,
            new TenantRolePermissionsRequestDto
            {
                ConcurrencyStamp = "stale", PermissionIds = Array.Empty<long>()
            });
        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(db.RolePermissions);
    }

    [Fact]
    public async Task Non_tenant_admin_is_denied_role_creation_and_membership_listing()
    {
        var access = ContextFor(100, isAdmin: false);
        await using var db = access.Db;
        var service = Service(db, access.User, access.Email);
        Assert.Equal(403, (await service.CreateRoleAsync(
            new TenantRoleCreateRequestDto { Name = "Teacher" })).StatusCode);
        Assert.Equal(403, (await service.GetMembersAsync(1, 20, null)).StatusCode);
        Assert.Empty(db.Roles);
    }

    [Fact]
    public async Task Cross_tenant_role_cannot_be_assigned_to_member_even_with_valid_token()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        await SeedSystemRolesAsync(db, access.Accessor, new ApplicationRole
        {
            Id = 222, TenantId = 200, Name = "Accountant",
            NormalizedName = "ACCOUNTANT", IsActive = true
        });
        var service = Service(db, access.User, access.Email);
        var result = await service.SetMemberRolesAsync(44,
            new TenantMemberRolesRequestDto
            {
                RowVersion = Convert.ToBase64String(new byte[8]), RoleIds = new[] { 222L }
            });
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(db.UserRoles);
    }

    [Fact]
    public async Task Legacy_global_grants_are_listed_for_review_and_require_explicit_removal_approval()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        await SeedSystemRolesAsync(db, access.Accessor, new ApplicationRole
        {
            Id = 93, TenantId = null, Name = "LegacyStaff", NormalizedName = "LEGACYSTAFF",
            IsSystemRole = false
        });
        db.Users.Add(new ApplicationUser { Id = 210, FullName = "Legacy member", Email = "legacy@example.test" });
        db.TenantMemberships.Add(new TenantMembership
        {
            Id = 300, TenantId = 100, UserId = 210, Status = EduOS.Core.Enums.Domain.MembershipStatus.Active,
            RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 210, RoleId = 93 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = Service(db, access.User, access.Email);
        var list = await service.GetMembersAsync(1, 20, null);
        Assert.True(list.Success);
        var member = Assert.Single(list.Data!.Items);
        Assert.Equal(new[] { "LegacyStaff" }, member.UnscopedIdentityRoleNames);
        Assert.True(member.CanRemoveUnscopedIdentityRoles);

        var attempt = await service.SetMemberRolesAsync(member.MembershipId,
            new TenantMemberRolesRequestDto { RowVersion = member.RowVersion, RoleIds = Array.Empty<long>() });
        Assert.False(attempt.Success);
        Assert.Equal(409, attempt.StatusCode);
        Assert.Contains("explicitly approve", attempt.Message);
        Assert.Single(db.UserRoles);
    }

    [Fact]
    public async Task Legacy_grants_for_members_with_multiple_institutions_are_not_marked_for_local_removal()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        await SeedSystemRolesAsync(db, access.Accessor, new ApplicationRole
        {
            Id = 94, Name = "OldTeacher", NormalizedName = "OLDTEACHER"
        });
        db.Users.Add(new ApplicationUser { Id = 211, FullName = "Shared member", Email = "shared@example.test" });
        db.TenantMemberships.Add(new TenantMembership { TenantId = 100, UserId = 211 });
        await db.SaveChangesAsync();
        var principal = access.Accessor.HttpContext!.User;
        var oldTenant = access.Accessor.HttpContext!.Items["TenantId"];
        try
        {
            access.Accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            access.Accessor.HttpContext.Items.Remove("TenantId");
            db.TenantMemberships.Add(new TenantMembership { TenantId = 200, UserId = 211 });
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = 211, RoleId = 94 });
            await db.SaveChangesAsync();
        }
        finally
        {
            access.Accessor.HttpContext.User = principal;
            access.Accessor.HttpContext.Items["TenantId"] = oldTenant;
        }
        db.ChangeTracker.Clear();
        var list = await Service(db, access.User, access.Email).GetMembersAsync(1, 20, null);
        var member = Assert.Single(list.Data!.Items);
        Assert.Equal(new[] { "OldTeacher" }, member.UnscopedIdentityRoleNames);
        Assert.False(member.CanRemoveUnscopedIdentityRoles);
    }

    [Theory]
    [InlineData(nameof(TenantUsersController.CreateRole))]
    [InlineData(nameof(TenantUsersController.SetRoles))]
    [InlineData(nameof(TenantUsersController.SetStatus))]
    [InlineData(nameof(TenantUsersController.SetPermissions))]
    [InlineData(nameof(TenantUsersController.SendInvitation))]
    public void Tenant_user_management_mutations_are_protected(string methodName)
    {
        var controller = typeof(TenantUsersController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute), true));
        var action = controller.GetMethod(methodName);
        Assert.NotNull(action);
        Assert.Empty(action!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Invitation_acceptance_is_public_but_individually_rate_limited()
    {
        var action = typeof(TenantUsersController).GetMethod(nameof(TenantUsersController.AcceptInvitation))!;
        Assert.NotEmpty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true));
    }

    [Fact]
    public async Task Member_access_history_is_tenant_scoped_paged_and_limited_to_access_events()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Users.Add(new ApplicationUser { Id = 210, FullName = "Staff", Email = "staff@example.test" });
        db.TenantMemberships.Add(new TenantMembership { Id = 300, TenantId = 100, UserId = 210 });
        var now = DateTime.UtcNow;
        db.AuditLogs.AddRange(
            new AuditLog { Id = 1, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberRolesChanged", UserName = "Admin", OldValue = "[1]", NewValue = "[2]",
                OccurredAt = now.AddMinutes(-2) },
            new AuditLog { Id = 2, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberCampusesChanged", NewValue = "{\\\"campuses\\\":[10]}", OccurredAt = now.AddMinutes(-1) },
            new AuditLog { Id = 3, TenantId = 200, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberRolesChanged" },
            new AuditLog { Id = 4, TenantId = 100, EntityName = "Student", EntityId = 300,
                Action = "TenantMemberRolesChanged" },
            new AuditLog { Id = 5, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberStatusChanged", IsSuccess = false },
            new AuditLog { Id = 6, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "UnrelatedAction" });
        await db.SaveChangesAsync();
        var service = Service(db, access.User, access.Email);
        var first = await service.GetMemberAccessHistoryAsync(300, 1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.Equal("TenantMemberCampusesChanged", Assert.Single(first.Data.Items).Action);
        var second = await service.GetMemberAccessHistoryAsync(300, 2, 1);
        Assert.Equal("TenantMemberRolesChanged", Assert.Single(second.Data!.Items).Action);
        Assert.Equal(400, (await service.GetMemberAccessHistoryAsync(300, int.MaxValue, 100)).StatusCode);
        Assert.Equal(404, (await service.GetMemberAccessHistoryAsync(999, 1, 20)).StatusCode);
        Assert.Equal(403, (await Service(db, ContextFor(100, false).User, access.Email)
            .GetMemberAccessHistoryAsync(300, 1, 20)).StatusCode);
    }

    [Fact]
    public void Member_history_api_is_authorized_and_exposes_paged_readback()
    {
        var method = typeof(TenantUsersController).GetMethod(nameof(TenantUsersController.MemberAccessHistory))!;
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), true));
    }


    [Fact]
    public async Task Member_access_history_csv_export_is_scoped_bounded_and_formula_safe()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Users.Add(new ApplicationUser { Id = 210, FullName = "Staff", Email = "staff@example.test" });
        db.TenantMemberships.Add(new TenantMembership { Id = 300, TenantId = 100, UserId = 210 });
        db.AuditLogs.AddRange(
            new AuditLog { Id = 1, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberRolesChanged", UserName = "=HYPERLINK(\"unsafe\")",
                OldValue = "\t=SUM(1,1)", NewValue = "+cmd", OccurredAt = DateTime.UtcNow },
            new AuditLog { Id = 2, TenantId = 200, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "TenantMemberRolesChanged", UserName = "Foreign", OccurredAt = DateTime.UtcNow },
            new AuditLog { Id = 3, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
                Action = "UnrelatedAction", UserName = "Not exported", OccurredAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = Service(db, access.User, access.Email);
        var export = await service.ExportMemberAccessHistoryCsvAsync(300);
        Assert.True(export.Success, export.Message);
        Assert.Contains("TenantMemberRolesChanged", export.Data);
        Assert.Contains("'=HYPERLINK", export.Data);
        Assert.Contains("'\t=SUM", export.Data);
        Assert.Contains("'+cmd", export.Data);
        Assert.DoesNotContain("Foreign", export.Data);
        Assert.DoesNotContain("Not exported", export.Data);
        Assert.Equal(404, (await service.ExportMemberAccessHistoryCsvAsync(999)).StatusCode);
        Assert.Equal(400, (await service.ExportMemberAccessHistoryCsvAsync(-1)).StatusCode);
        Assert.Equal(403, (await Service(db, ContextFor(100, false).User, access.Email)
            .ExportMemberAccessHistoryCsvAsync(300)).StatusCode);
    }

    [Fact]
    public async Task Member_access_history_csv_rejects_more_than_one_thousand_events()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Users.Add(new ApplicationUser { Id = 210, FullName = "Staff", Email = "staff@example.test" });
        db.TenantMemberships.Add(new TenantMembership { Id = 300, TenantId = 100, UserId = 210 });
        db.AuditLogs.AddRange(Enumerable.Range(1, 1001).Select(i => new AuditLog
        {
            Id = i, TenantId = 100, EntityName = nameof(TenantMembership), EntityId = 300,
            Action = "TenantMemberStatusChanged", OccurredAt = DateTime.UtcNow.AddSeconds(-i)
        }));
        await db.SaveChangesAsync();
        var export = await Service(db, access.User, access.Email).ExportMemberAccessHistoryCsvAsync(300);
        Assert.Equal(413, export.StatusCode);
        Assert.False(export.Success);
    }

    [Fact]
    public void Member_access_history_export_endpoint_is_tenant_admin_protected()
    {
        var controller = typeof(TenantUsersController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        var action = controller.GetMethod(nameof(TenantUsersController.ExportMemberAccessHistory))!;
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), true));
    }

    private static (EduOSDbContext Db, Mock<ICurrentUserService> User, Mock<IEmailService> Email, HttpContextAccessor Accessor)
        ContextFor(long tenantId, bool isAdmin)
    {
        var access = new HttpContextAccessor();
        var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("tenant-users-" + Guid.NewGuid().ToString("N")).Options, access);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Role, isAdmin ? "TenantAdmin" : "Teacher")
        }, "Test"));
        http.Items["TenantId"] = tenantId;
        access.HttpContext = http;
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.IsTenantAdmin).Returns(isAdmin);
        current.SetupGet(x => x.TenantId).Returns(tenantId);
        current.SetupGet(x => x.UserId).Returns(42);
        return (db, current, new Mock<IEmailService>(), access);
    }

    private static async Task SeedSystemRolesAsync(EduOSDbContext db, HttpContextAccessor accessor,
        params ApplicationRole[] roles)
    {
        // Simulate trusted platform provisioning without weakening tenant guards.
        // A tenant administrator must never insert a role owned by another tenant.
        var http = accessor.HttpContext!;
        var priorPrincipal = http.User;
        var priorTenant = http.Items["TenantId"];
        try
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            http.Items.Remove("TenantId");
            db.Roles.AddRange(roles);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        finally
        {
            http.User = priorPrincipal;
            http.Items["TenantId"] = priorTenant;
        }
    }

    private static TenantUserAdministrationService Service(
        EduOSDbContext db, Mock<ICurrentUserService> user, Mock<IEmailService> email)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var identity = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        return new TenantUserAdministrationService(db, user.Object, identity.Object, email.Object,
            Options.Create(new TenantPortalSettings { BaseDomain = "portal.example.test" }));
    }
}
