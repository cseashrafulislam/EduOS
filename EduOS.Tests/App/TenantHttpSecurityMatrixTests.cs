using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Middleware;
using EduOS.App.Security;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Runs the production session and tenant middleware in a real HTTP pipeline.
/// Test-only header authentication supplies signed-cookie-equivalent claims; the
/// user, role, tenant, membership and session records are resolved from the DB.
/// This is an integration contract, not a substitute for browser/cookie UAT.
/// </summary>
public sealed class TenantHttpSecurityMatrixTests
{
    private const long UserId = 501;
    private const long TenantA = 101;
    private const long TenantB = 202;

    private sealed class HeaderCookieFixtureAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var sid = Request.Headers["X-Test-Session"].ToString();
            if (string.IsNullOrEmpty(sid))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, UserId.ToString()),
                new(AuthSessionRegistry.ClaimType, sid)
            };
            var tenant = Request.Headers["X-Test-Tenant"].ToString();
            if (tenant.Length > 0) claims.Add(new Claim("TenantId", tenant));
            var role = Request.Headers["X-Test-Role"].ToString();
            foreach (var part in role.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                claims.Add(new Claim(ClaimTypes.Role, part));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    [Fact]
    public async Task Real_http_session_and_tenant_pipeline_rejects_cross_institution_replay_and_stale_roles()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("http-tenant-matrix-" + Guid.NewGuid().ToString("N"),
                new InMemoryDatabaseRoot()).Options;
        var account = new ApplicationUser
        {
            Id = UserId, UserName = "matrix@example.test", Email = "matrix@example.test",
            EmailConfirmed = true, IsActive = true
        };
        string original;
        await using (var seed = new EduOSDbContext(options))
        {
            seed.Tenants.AddRange(
                new Tenant { Id = TenantA, Name = "Matrix A", Code = "MATRIX-A", Email = "a@example.test", State = TenantState.Active },
                new Tenant { Id = TenantB, Name = "Matrix B", Code = "MATRIX-B", Email = "b@example.test", State = TenantState.Active });
            seed.Users.Add(account);
            await seed.SaveChangesAsync();
            using (seed.BeginSystemTenantScope(TenantA))
            {
                seed.Roles.Add(new ApplicationRole
                {
                    Id = 1001, TenantId = TenantA, Name = "Teacher", NormalizedName = "TEACHER", IsActive = true
                });
                seed.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = TenantA, UserId = UserId, Status = MembershipStatus.Active
                });
                await seed.SaveChangesAsync();
            }
            seed.ChangeTracker.Clear();
            using (seed.BeginSystemTenantScope(TenantB))
            {
                seed.Roles.Add(new ApplicationRole
                {
                    Id = 2002, TenantId = TenantB, Name = "Accountant", NormalizedName = "ACCOUNTANT", IsActive = true
                });
                seed.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = TenantB, UserId = UserId, Status = MembershipStatus.Active
                });
                await seed.SaveChangesAsync();
            }
            seed.ChangeTracker.Clear();
            seed.UserRoles.AddRange(
                new IdentityUserRole<long> { UserId = UserId, RoleId = 1001 },
                new IdentityUserRole<long> { UserId = UserId, RoleId = 2002 });
            await seed.SaveChangesAsync();
            original = await AuthSessionRegistry.CreateAsync(seed, UserId, TenantA, "", "test", false);
        }

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(store.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.FindByIdAsync(UserId.ToString())).ReturnsAsync(account);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddHttpContextAccessor();
                services.AddAuthentication("Matrix")
                    .AddScheme<AuthenticationSchemeOptions, HeaderCookieFixtureAuthentication>("Matrix", _ => { });
                services.AddSingleton(userManager.Object);
                services.AddScoped(sp => new EduOSDbContext(options,
                    sp.GetRequiredService<IHttpContextAccessor>()));
            })
            .Configure(app =>
            {
                app.UseAuthentication();
                app.UseAuthSessionGuard();
                app.UseTenantContext();
                app.Run(async context => await context.Response.WriteAsync(
                    "tenant=" + (context.Items["TenantId"]?.ToString() ?? "none")));
            }));
        using var client = server.CreateClient();
        async Task<HttpResponseMessage> Read(string sid, long tenant, string role)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/private/membership-check");
            request.Headers.Add("X-Test-Session", sid);
            request.Headers.Add("X-Test-Tenant", tenant.ToString());
            request.Headers.Add("X-Test-Role", role);
            return await client.SendAsync(request);
        }

        using (var authorized = await Read(original, TenantA, "Teacher"))
        {
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
            Assert.Equal("tenant=101", await authorized.Content.ReadAsStringAsync());
        }
        using (var sidFromWrongTenant = await Read(original, TenantB, "Accountant"))
            Assert.Equal(HttpStatusCode.Unauthorized, sidFromWrongTenant.StatusCode);
        using (var forgedAdmin = await Read(original, TenantA, "TenantAdmin"))
            Assert.Equal(HttpStatusCode.Unauthorized, forgedAdmin.StatusCode);

        await using (var change = new EduOSDbContext(options))
        {
            using (change.BeginSystemTenantScope(TenantA))
            {
                var membership = await change.TenantMemberships.SingleAsync(x => x.UserId == UserId);
                membership.Status = MembershipStatus.Suspended;
                await change.SaveChangesAsync();
            }
        }
        using (var suspended = await Read(original, TenantA, "Teacher"))
            Assert.Equal(HttpStatusCode.Forbidden, suspended.StatusCode);

        await using (var change = new EduOSDbContext(options))
        {
            using (change.BeginSystemTenantScope(TenantA))
            {
                var membership = await change.TenantMemberships.SingleAsync(x => x.UserId == UserId);
                membership.Status = MembershipStatus.Active;
                var role = await change.Roles.SingleAsync(x => x.Id == 1001);
                role.IsActive = false;
                await change.SaveChangesAsync();
            }
        }
        using (var revokedRole = await Read(original, TenantA, "Teacher"))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedRole.StatusCode);

        await using (var change = new EduOSDbContext(options))
        {
            using (change.BeginSystemTenantScope(TenantA))
            {
                var role = await change.Roles.SingleAsync(x => x.Id == 1001);
                role.IsActive = true;
                await change.SaveChangesAsync();
            }
        }
        account.IsActive = false;
        using (var inactiveAccount = await Read(original, TenantA, "Teacher"))
            Assert.Equal(HttpStatusCode.Forbidden, inactiveAccount.StatusCode);
        account.IsActive = true;

        string? switched;
        await using (var change = new EduOSDbContext(options))
            switched = await AuthSessionRegistry.RotateAsync(change, UserId, original, TenantA, TenantB,
                "", "switched", false);
        Assert.NotNull(switched);
        using (var oldCookie = await Read(original, TenantA, "Teacher"))
            Assert.Equal(HttpStatusCode.Unauthorized, oldCookie.StatusCode);
        using (var movedCookieOldTenant = await Read(switched!, TenantA, "Teacher"))
            Assert.Equal(HttpStatusCode.Unauthorized, movedCookieOldTenant.StatusCode);
        using (var switchedTenant = await Read(switched!, TenantB, "Accountant"))
        {
            Assert.Equal(HttpStatusCode.OK, switchedTenant.StatusCode);
            Assert.Equal("tenant=202", await switchedTenant.Content.ReadAsStringAsync());
        }
    }
}
