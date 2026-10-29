using EduOS.App.Middleware;
using EduOS.App.Security;
using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AuthTenantSessionIntegrationTests
{
    private static EduOSDbContext Db() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("auth-tenant-session-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task Tenant_role_grants_do_not_cross_memberships()
    {
        await using var db = Db();
        db.Roles.AddRange(
            new ApplicationRole { Id = 1, TenantId = null, Name = "Principal", NormalizedName = "PRINCIPAL", IsActive = true },
            new ApplicationRole { Id = 2, TenantId = 200, Name = "Teacher", NormalizedName = "TEACHER", IsActive = true });
        db.UserRoles.AddRange(
            new IdentityUserRole<long> { UserId = 50, RoleId = 1 },
            new IdentityUserRole<long> { UserId = 50, RoleId = 2 });
        await db.SaveChangesAsync();

        var tenantOne = await TenantRoleResolver.GetTenantRolesAsync(db, 50, 100, false, 2);
        var tenantTwo = await TenantRoleResolver.GetTenantRolesAsync(db, 50, 200, false, 2);
        Assert.Equal(new[] { "User" }, tenantOne);
        Assert.Contains("Teacher", tenantTwo);
        Assert.DoesNotContain("Principal", tenantTwo);
        Assert.DoesNotContain("Principal", tenantOne);
    }

    [Fact]
    public async Task Membership_ownership_grants_admin_only_for_selected_institution()
    {
        await using var db = Db();
        var ownerRoles = await TenantRoleResolver.GetTenantRolesAsync(db, 50, 100, true, 2);
        var visitorRoles = await TenantRoleResolver.GetTenantRolesAsync(db, 50, 200, false, 2);
        Assert.Contains("TenantAdmin", ownerRoles);
        Assert.DoesNotContain("TenantAdmin", visitorRoles);
    }

    [Fact]
    public async Task Tenant_scoped_super_admin_name_never_grants_platform_admin()
    {
        await using var db = Db();
        db.Roles.Add(new ApplicationRole
        {
            Id = 1, TenantId = 100, Name = "SuperAdmin",
            NormalizedName = "SUPERADMIN", IsActive = true
        });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 50, RoleId = 1 });
        await db.SaveChangesAsync();
        Assert.False(await TenantRoleResolver.IsPlatformAdminAsync(db, 50));
        var roles = await TenantRoleResolver.GetTenantRolesAsync(db, 50, 100, false, 1);
        Assert.DoesNotContain("SuperAdmin", roles);
    }

    [Fact]
    public async Task Legacy_global_role_only_applies_to_single_membership()
    {
        await using var db = Db();
        db.Roles.Add(new ApplicationRole
        {
            Id = 1, TenantId = null, Name = "Teacher",
            NormalizedName = "TEACHER", IsActive = true
        });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 50, RoleId = 1 });
        await db.SaveChangesAsync();
        Assert.Contains("Teacher", await TenantRoleResolver.GetTenantRolesAsync(db, 50, 100, false, 1));
        Assert.DoesNotContain("Teacher", await TenantRoleResolver.GetTenantRolesAsync(db, 50, 100, false, 2));
    }


    [Fact]
    public async Task Sensitive_account_session_readback_rejects_revoked_cross_tenant_and_other_user_cookies()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        ClaimsPrincipal Principal(long userId, string? tenant, string session) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(AuthSessionRegistry.ClaimType, session)
            }.Concat(tenant == null ? Array.Empty<Claim>() : new[] { new Claim("TenantId", tenant) }), "Test"));

        Assert.True(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(50, "100", sid)));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(50, "200", sid)));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(51, "100", sid)));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(50, "invalid", sid)));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(50, null, sid)));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, new ClaimsPrincipal()));
        await AuthSessionRegistry.RevokeAsync(db, 50, sid);
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, Principal(50, "100", sid)));
    }

    [Fact]
    public async Task Platform_account_session_readback_requires_the_original_unassigned_session()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 80, null, "", "", false);
        var platform = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "80"),
            new Claim(AuthSessionRegistry.ClaimType, sid)
        }, "Test"));
        Assert.True(await AuthSessionRegistry.IsValidPrincipalAsync(db, platform));
        var impersonatedTenant = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "80"),
            new Claim(AuthSessionRegistry.ClaimType, sid),
            new Claim("TenantId", "100")
        }, "Test"));
        Assert.False(await AuthSessionRegistry.IsValidPrincipalAsync(db, impersonatedTenant));
    }

    [Fact]
    public void Account_profile_and_membership_readbacks_revalidate_after_sensitive_queries()
    {
        var auth = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        var switching = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthInstitutionSwitchController.cs"));
        var mfa = auth[auth.IndexOf("public async Task<IActionResult> MfaStatus()", StringComparison.Ordinal)..
            auth.IndexOf("// Refresh the antiforgery token", StringComparison.Ordinal)];
        var sessions = auth[auth.IndexOf("public async Task<IActionResult> GetSessions()", StringComparison.Ordinal)..
            auth.IndexOf("[HttpPost(\"sessions/revoke\")]", StringComparison.Ordinal)];
        var profile = auth[auth.IndexOf("public async Task<IActionResult> GetProfile()", StringComparison.Ordinal)..
            auth.IndexOf("[HttpPut(\"profile\")]", StringComparison.Ordinal)];
        var update = auth[auth.IndexOf("public async Task<IActionResult> UpdateProfile(", StringComparison.Ordinal)..
            auth.IndexOf("// ==================== Change Password", StringComparison.Ordinal)];
        var choices = switching[switching.IndexOf("public async Task<IActionResult> GetAvailableInstitutions()", StringComparison.Ordinal)..
            switching.IndexOf("// This read-only, no-store snapshot", StringComparison.Ordinal)];

        Assert.Contains("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)", mfa);
        Assert.Contains("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)", sessions);
        Assert.Contains("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)", profile);
        Assert.Equal(2, update.Split("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)").Length - 1);
        Assert.Contains("System.Data.IsolationLevel.Serializable", update);
        Assert.Contains("catch (AccountSessionChangedException)", update);
        Assert.Equal(2, choices.Split("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)").Length - 1);
    }

    [Fact]
    public async Task Authenticated_sessions_are_scoped_revocable_and_cannot_be_used_by_another_user_or_tenant()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "127.0.0.1", "browser", false);
        Assert.Equal(48, sid.Length);
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, sid, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 51, sid, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, sid, 200));
        var sessions = await AuthSessionRegistry.ListAsync(db, 50, sid);
        Assert.Single(sessions);
        Assert.True(await AuthSessionRegistry.RevokeAsync(db, 50, sid));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, sid, 100));
        Assert.False(await AuthSessionRegistry.RevokeAsync(db, 51, sid));
    }

    [Fact]
    public async Task Revoke_other_devices_keeps_only_the_current_tenant_bound_cookie()
    {
        await using var db = Db();
        var current = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var sameTenant = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var anotherTenant = await AuthSessionRegistry.CreateAsync(db, 50, 200, "", "", false);
        var anotherUser = await AuthSessionRegistry.CreateAsync(db, 51, 100, "", "", false);
        Assert.Null(await AuthSessionRegistry.RevokeOthersAsync(db, 50, current, 200));
        Assert.Null(await AuthSessionRegistry.RevokeOthersAsync(db, 51, current, 100));
        Assert.Equal(2, await AuthSessionRegistry.RevokeOthersAsync(db, 50, current, 100));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, current, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, sameTenant, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, anotherTenant, 200));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 51, anotherUser, 100));
        Assert.Equal(0, await AuthSessionRegistry.RevokeOthersAsync(db, 50, current, 100));
        Assert.Null(await AuthSessionRegistry.RevokeOthersAsync(db, 50, sameTenant, 100));
    }

    [Fact]
    public async Task Institution_switch_rotates_the_cookie_and_rejects_replay_or_cross_tenant_use()
    {
        await using var db = Db();
        var original = await AuthSessionRegistry.CreateAsync(db, 50, 100, "127.0.0.1", "first", true);
        var anotherDevice = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "second", false);
        var anotherUser = await AuthSessionRegistry.CreateAsync(db, 51, 100, "", "other", false);

        Assert.Null(await AuthSessionRegistry.RotateAsync(db, 50, original, 200, 300, "", "", true));
        Assert.Null(await AuthSessionRegistry.RotateAsync(db, 51, original, 100, 200, "", "", true));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, original, 100));

        var rotated = await AuthSessionRegistry.RotateAsync(db, 50, original, 100, 200,
            "127.0.0.1", "switched", true);
        Assert.NotNull(rotated);
        Assert.NotEqual(original, rotated);
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, original, 100));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, rotated, 200));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, rotated, 100));
        Assert.Null(await AuthSessionRegistry.RotateAsync(db, 50, original, 100, 300, "", "", true));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, anotherDevice, 100));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 51, anotherUser, 100));
        Assert.Equal(2, await db.UserTokens.CountAsync(x => x.UserId == 50 &&
            x.LoginProvider == AuthSessionRegistry.Provider));
    }

    [Fact]
    public async Task Public_session_references_hide_cookie_ids_and_revoke_only_the_owner_session()
    {
        await using var db = Db();
        var current = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var other = await AuthSessionRegistry.CreateAsync(db, 50, 200, "", "", false);
        var stranger = await AuthSessionRegistry.CreateAsync(db, 51, 100, "", "", false);
        var reference = AuthSessionRegistry.ToPublicReference(other);
        Assert.Equal(64, reference.Length);
        Assert.NotEqual(other, reference);
        Assert.True(AuthSessionRegistry.IsCurrentReference(other, reference));
        Assert.False(AuthSessionRegistry.IsCurrentReference(current, reference));
        var json = System.Text.Json.JsonSerializer.Serialize(await AuthSessionRegistry.ListAsync(db, 50, current));
        Assert.Contains(reference, json);
        Assert.DoesNotContain(current, json);
        Assert.DoesNotContain(other, json);
        Assert.False(await AuthSessionRegistry.RevokeByReferenceAsync(db, 51, reference));
        Assert.False(await AuthSessionRegistry.RevokeByReferenceAsync(db, 50, other));
        Assert.False(await AuthSessionRegistry.RevokeByReferenceAsync(db, 50, new string('A', 64)));
        Assert.True(await AuthSessionRegistry.RevokeByReferenceAsync(db, 50, reference));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, other, 200));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, current, 100));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 51, stranger, 100));
        Assert.False(await AuthSessionRegistry.RevokeByReferenceAsync(db, 50, reference));
    }

    [Fact]
    public async Task Revoking_all_sessions_invalidates_all_cookie_ids_for_a_user_only()
    {
        await using var db = Db();
        var first = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var second = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", true);
        var other = await AuthSessionRegistry.CreateAsync(db, 51, 100, "", "", false);
        await AuthSessionRegistry.RevokeAllAsync(db, 50);
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, first, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, second, 100));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 51, other, 100));
    }

    [Fact]
    public async Task Expired_or_missing_session_is_not_accepted()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var token = await db.UserTokens.SingleAsync(x => x.UserId == 50 && x.Name == sid);
        token.Value = System.Text.Json.JsonSerializer.Serialize(new AuthSessionRegistry.SessionRecord
        {
            TenantId = 100, CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, sid, 100));
        Assert.False(await AuthSessionRegistry.IsValidAsync(db, 50, "invalid", 100));
    }


    [Fact]
    public async Task Stale_request_after_institution_rotation_cannot_clear_the_new_cookie()
    {
        await using var db = Db();
        var oldSid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var newSid = await AuthSessionRegistry.RotateAsync(db, 50, oldSid, 100, 200, "", "", false);
        Assert.NotNull(newSid);
        var middlewareCalls = 0;
        var middleware = new AuthSessionMiddleware(_ =>
        {
            middlewareCalls++;
            return Task.CompletedTask;
        });

        var stale = new DefaultHttpContext();
        stale.Request.Path = "/api/auth/session-context";
        stale.RequestServices = new ServiceCollection()
            .AddOptions().BuildServiceProvider();
        stale.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "50"),
            new Claim("TenantId", "100"),
            new Claim(AuthSessionRegistry.ClaimType, oldSid)
        }, "Test"));
        await middleware.InvokeAsync(stale, db);
        Assert.Equal(StatusCodes.Status401Unauthorized, stale.Response.StatusCode);
        Assert.False(stale.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal("no-store", stale.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, middlewareCalls);

        // The old response must not invalidate the replacement institution session.
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, 50, newSid, 200));
        var current = new DefaultHttpContext();
        current.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "50"),
            new Claim("TenantId", "200"),
            new Claim(AuthSessionRegistry.ClaimType, newSid!)
        }, "Test"));
        await middleware.InvokeAsync(current, db);
        Assert.Equal(1, middlewareCalls);
    }

    [Fact]
    public async Task Middleware_allows_only_a_valid_cookie_bound_to_the_same_institution()
    {
        await using var db = Db();
        var sid = await AuthSessionRegistry.CreateAsync(db, 50, 100, "", "", false);
        var calls = 0;
        var middleware = new AuthSessionMiddleware(_ => { calls++; return Task.CompletedTask; });
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "50"),
            new Claim("TenantId", "100"),
            new Claim(AuthSessionRegistry.ClaimType, sid)
        }, "Test"));
        await middleware.InvokeAsync(http, db);
        Assert.Equal(1, calls);
    }
}
