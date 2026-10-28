using EduOS.App.Controllers;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Proves the session check and preference write share a real SQL transaction.
/// The same-context revocation simulates a session change between SaveChanges
/// and response validation; both writes must roll back on that mismatch.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerWorkspacePreferenceSessionIntegrationTests
{
    [Fact]
    public async Task Revoked_session_during_preference_save_rolls_back_setting_and_session_change()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var http = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = http };
        await using var db = new EduOSDbContext(options, accessor);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var tenant = new Tenant
        {
            Name = "CI Workspace " + suffix,
            Code = "CI-WSP-" + suffix,
            Email = "wsp-" + suffix + "@example.test",
            State = TenantState.Active
        };
        var email = "wsp-user-" + suffix + "@example.test";
        var user = new ApplicationUser
        {
            UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Workspace SQL Test", EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        http.Items["TenantId"] = tenant.Id;

        var sid = await AuthSessionRegistry.CreateAsync(db, user.Id, tenant.Id, "", "", false);
        var actual = new WorkspacePreferencesService(
            new GenericRepository<TenantSetting>(db), db,
            new WorkspaceUser(tenant.Id, user.Id), NullLogger<WorkspacePreferencesService>.Instance);
        var service = new RevokeAfterSaveService(actual, () =>
            AuthSessionRegistry.RevokeAsync(db, user.Id, sid));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("TenantId", tenant.Id.ToString()),
            new Claim(AuthSessionRegistry.ClaimType, sid)
        };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
        var controller = new WorkspacePreferencesController(service, db)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var response = await controller.Save(new SaveWorkspacePreferencesRequestDto
        {
            Theme = "dark"
        }, default);
        Assert.IsType<UnauthorizedObjectResult>(response);

        // The rejected write must not persist, and rollback must also restore the
        // session row deleted by the test's in-transaction simulated revocation.
        Assert.False(await db.TenantSettings.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant.Id &&
                x.Key == WorkspacePreferencesService.KeyPrefix + user.Id));
        Assert.True(await AuthSessionRegistry.IsValidAsync(db, user.Id, sid, tenant.Id));
    }

    private sealed class RevokeAfterSaveService(
        IWorkspacePreferencesService inner, Func<Task<bool>> revoke) : IWorkspacePreferencesService
    {
        public Task<ApiResponse<WorkspacePreferencesDto>> GetAsync(CancellationToken ct = default) =>
            inner.GetAsync(ct);

        public async Task<ApiResponse<WorkspacePreferencesDto>> SaveAsync(
            SaveWorkspacePreferencesRequestDto request, CancellationToken ct = default)
        {
            var result = await inner.SaveAsync(request, ct);
            Assert.True(result.Success, result.Message);
            Assert.True(await revoke());
            return result;
        }

        public Task<ApiResponse<WorkspacePreferencesDto>> ResetAsync(
            ResetWorkspacePreferencesRequestDto request, CancellationToken ct = default) =>
            inner.ResetAsync(request, ct);
    }

    private sealed class WorkspaceUser(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Workspace SQL Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
