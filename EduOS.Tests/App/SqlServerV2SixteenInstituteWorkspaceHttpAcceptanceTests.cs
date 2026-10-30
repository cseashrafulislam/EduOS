using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server + versioned workspace service + tenant-bound session + HTTP
/// readback across the 16-institute release cohort. No mocked preference data.
/// This is a security/personalization integration gate, not browser/mobile UAT.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record WorkspaceMember(Fixture Fixture, string Session, string Theme, string Navigation);

    private sealed class WorkspaceCohortAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var tenant = Request.Headers["X-Cohort-Tenant"].ToString();
            var user = Request.Headers["X-Cohort-User"].ToString();
            var session = Request.Headers["X-Cohort-Session"].ToString();
            if (!long.TryParse(tenant, out var tenantId) || tenantId <= 0 ||
                !long.TryParse(user, out var userId) || userId <= 0 ||
                session.Length != 48)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant),
                new Claim(AuthSessionRegistry.ClaimType, session)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class WorkspaceCohortUser(IHttpContextAccessor accessor) : ICurrentUserService
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public long UserId => long.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        public long TenantId => long.TryParse(Principal?.FindFirstValue("TenantId"), out var id) ? id : 0;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => IsInRole("TenantAdmin");
        public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray() ?? [];
        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static TestServer WorkspaceCohortServer(DbContextOptions<EduOSDbContext> options) =>
        new(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("WorkspaceCohort")
                    .AddScheme<AuthenticationSchemeOptions, WorkspaceCohortAuth>("WorkspaceCohort", _ => { });
                services.AddAuthorization();
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, WorkspaceCohortUser>();
                services.AddScoped<IWorkspacePreferencesService>(sp => new WorkspacePreferencesService(
                    new GenericRepository<TenantSetting>(sp.GetRequiredService<EduOSDbContext>()),
                    sp.GetRequiredService<EduOSDbContext>(),
                    sp.GetRequiredService<ICurrentUserService>(),
                    NullLogger<WorkspacePreferencesService>.Instance));
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(WorkspacePreferencesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.Use(async (http, next) =>
                {
                    if (http.User.Identity?.IsAuthenticated == true &&
                        long.TryParse(http.User.FindFirstValue("TenantId"), out var tenant))
                        http.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

    private static WorkspacePreferencesController WorkspaceController(
        EduOSDbContext db, long tenantId, long userId, string session)
    {
        var service = new WorkspacePreferencesService(
            new GenericRepository<TenantSetting>(db), db,
            new TenantAdmin(tenantId, userId),
            NullLogger<WorkspacePreferencesService>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("TenantId", tenantId.ToString()),
            new Claim(AuthSessionRegistry.ClaimType, session),
            new Claim(ClaimTypes.Role, "TenantAdmin")
        ], "WorkspaceCohort"));
        return new WorkspacePreferencesController(service, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static WorkspacePreferencesDto Saved(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, objectResult.StatusCode);
        var response = Assert.IsType<ApiResponse<WorkspacePreferencesDto>>(objectResult.Value);
        Assert.True(response.Success, response.Message);
        return Assert.IsType<WorkspacePreferencesDto>(response.Data);
    }

    private static async Task<HttpResponseMessage> WorkspaceHttpGetAsync(
        HttpClient client, long tenant, long user, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspace-preferences");
        request.Headers.TryAddWithoutValidation("X-Cohort-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Cohort-User", user.ToString());
        request.Headers.TryAddWithoutValidation("X-Cohort-Session", session);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Sixteen_SQL_institutes_keep_same_users_preferences_session_and_versions_isolated()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var cohort = new List<Fixture>(16);
        for (var i = 0; i < 16; i++)
            cohort.Add(await SeedAsync(connection));
        Assert.Equal(16, cohort.Select(x => x.TenantId).Distinct().Count());
        var userId = cohort[0].UserId;
        var members = new List<WorkspaceMember>(16);

        // Explicit active memberships: one real identity belongs to all 16 SQL tenants.
        foreach (var (fixture, index) in cohort.Select((value, index) => (value, index)))
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            if (!await db.TenantMemberships.AnyAsync(x => x.TenantId == fixture.TenantId && x.UserId == userId))
            {
                db.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = fixture.TenantId, UserId = userId,
                    Status = MembershipStatus.Active, JoinedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            var session = await AuthSessionRegistry.CreateAsync(
                db, userId, fixture.TenantId, "", "16-institute SQL integration", false);
            var theme = index % 2 == 0 ? "dark" : "light";
            var navigation = index % 3 == 0 ? "top" : index % 3 == 1 ? "right" : "left";
            var saved = Saved(await WorkspaceController(db, fixture.TenantId, userId, session)
                .Save(new SaveWorkspacePreferencesRequestDto
                {
                    Theme = theme,
                    Navigation = navigation,
                    Density = index % 2 == 0 ? "compact" : "comfortable",
                    Palette = index % 2 == 0 ? "teal" : "violet",
                    TablePreferences = TablePreferenceDto.Defaults()
                }, default));
            Assert.Equal(theme, saved.Theme);
            Assert.Equal(navigation, saved.Navigation);
            Assert.False(string.IsNullOrWhiteSpace(saved.RowVersion));
            members.Add(new WorkspaceMember(fixture, session, theme, navigation));
        }

        using var server = WorkspaceCohortServer(cohort[0].Options);
        using var client = server.CreateClient();

        // The same HTTP client switches institutions in both directions.
        foreach (var member in members.Concat(members.AsEnumerable().Reverse()))
        {
            using var response = await WorkspaceHttpGetAsync(
                client, member.Fixture.TenantId, userId, member.Session);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(member.Fixture.TenantId.ToString(),
                Assert.Single(response.Headers.GetValues("X-EduOS-Tenant-Id")));
            Assert.Equal(AuthSessionRegistry.ToPublicReference(member.Session),
                Assert.Single(response.Headers.GetValues("X-EduOS-Session-Reference")));
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(member.Theme, data.GetProperty("theme").GetString());
            Assert.Equal(member.Navigation, data.GetProperty("navigation").GetString());
            Assert.DoesNotContain(member.Session, json);
        }

        // A live cookie/session for one institution cannot be replayed with
        // another institution claim, even for the same user and valid membership.
        foreach (var index in Enumerable.Range(0, members.Count))
        {
            var caller = members[index];
            var foreign = members[(index + 1) % members.Count];
            using var denied = await WorkspaceHttpGetAsync(
                client, foreign.Fixture.TenantId, userId, caller.Session);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.DoesNotContain(foreign.Theme + "\"",
                await denied.Content.ReadAsStringAsync());
        }

        // A stale second device, invalid table visibility and revoked session
        // cannot change or read another tenant's preferences.
        var target = members[4];
        await using (var db = ScopedContext(target.Fixture.Options, target.Fixture.TenantId, target.Fixture.UserId))
        {
            var controller = WorkspaceController(db, target.Fixture.TenantId, userId, target.Session);
            var original = Saved(await controller.Get(default));
            var updated = Saved(await controller.Save(new SaveWorkspacePreferencesRequestDto
            {
                RowVersion = original.RowVersion, Theme = "light",
                Navigation = "right", TablePreferences = TablePreferenceDto.Defaults()
            }, default));
            Assert.NotEqual(original.RowVersion, updated.RowVersion);

            var stale = Assert.IsType<ObjectResult>(await controller.Save(
                new SaveWorkspacePreferencesRequestDto
                {
                    RowVersion = original.RowVersion, Theme = "dark",
                    TablePreferences = TablePreferenceDto.Defaults()
                }, default));
            Assert.Equal(409, stale.StatusCode);

            var tables = TablePreferenceDto.Defaults();
            tables[0].Columns.Single(x => x.Id == "action").Visible = false;
            var invalid = Assert.IsType<ObjectResult>(await controller.Save(
                new SaveWorkspacePreferencesRequestDto
                {
                    RowVersion = updated.RowVersion, Theme = "dark",
                    TablePreferences = tables
                }, default));
            Assert.Equal(400, invalid.StatusCode);
            Assert.Equal("light", Saved(await controller.Get(default)).Theme);

            var reset = Saved(await controller.Reset(new ResetWorkspacePreferencesRequestDto
            {
                RowVersion = updated.RowVersion
            }, default));
            Assert.Equal("system", reset.Theme);
            Assert.Equal("left", reset.Navigation);
            Assert.True(await AuthSessionRegistry.RevokeAsync(db, userId, target.Session));
        }
        using (var revoked = await WorkspaceHttpGetAsync(
            client, target.Fixture.TenantId, userId, target.Session))
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

        foreach (var member in members.Where(x => x.Fixture.TenantId != target.Fixture.TenantId))
        {
            using var response = await WorkspaceHttpGetAsync(
                client, member.Fixture.TenantId, userId, member.Session);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // SQL ownership and history are intact: exactly one user-scoped row
        // per institute, and a reset/revocation did not modify other tenants.
        foreach (var member in members)
        {
            await using var db = ScopedContext(member.Fixture.Options,
                member.Fixture.TenantId, member.Fixture.UserId);
            var rows = await db.TenantSettings.AsNoTracking()
                .Where(x => x.TenantId == member.Fixture.TenantId &&
                    x.Key == WorkspacePreferencesService.KeyPrefix + userId)
                .ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("UserWorkspace", row.Category);
            Assert.False(row.IsSensitive);
        }
    }
}
