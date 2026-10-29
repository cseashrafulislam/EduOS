using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Middleware;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// HTTP -> real service -> real SQL tenant filter -> private byte storage.
/// Uses disposable SQL fixtures and intercepted storage, not customer data.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed class PrivateHttpAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            var tenant = Request.Headers["X-Test-Tenant"].ToString();
            var user = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(role) ||
                !long.TryParse(tenant, out var t) || t <= 0 ||
                !long.TryParse(user, out var u) || u <= 0)
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenant)
            }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class PrivateHttpUser(IHttpContextAccessor accessor) : ICurrentUserService
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public long UserId => long.TryParse(
            Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var x) ? x : 0;
        public long TenantId => long.TryParse(
            Principal?.FindFirstValue("TenantId"), out var x) ? x : 0;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => IsInRole("SuperAdmin");
        public bool IsTenantAdmin => IsInRole("TenantAdmin");
        public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role)
            .Select(x => x.Value).ToArray() ?? [];
        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static TestServer PrivateSqlServer(
        DbContextOptions<EduOSDbContext> options, Mock<IFileUploadService> storage)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("SqlPrivateTest")
                    .AddScheme<AuthenticationSchemeOptions, PrivateHttpAuth>(
                        "SqlPrivateTest", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(modules.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, PrivateHttpUser>();
                services.AddScoped<ILibraryDigitalResourceService, LibraryDigitalResourceService>();
                services.AddSingleton(storage.Object);
                services.AddSingleton(Mock.Of<IDocumentFileService>());
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(LibraryDigitalResourcesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseV2PrivateApiResponseHeaders();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.Use(async (http, next) =>
                {
                    // Only a successfully authenticated test identity sets scope.
                    if (http.User.Identity?.IsAuthenticated == true &&
                        long.TryParse(http.User.FindFirstValue("TenantId"), out var tenant))
                        http.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(x => x.MapControllers());
            }));
    }

    private static async Task<HttpResponseMessage> PrivateGetAsync(
        HttpClient client, Guid id, string? role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/library/digital/{id}/download");
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    private static void AssertPrivateCache(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task SQL_HTTP_private_download_denies_foreign_tenant_guessed_ID_and_V1_revocations()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var first = await SeedAsync(connection);
        var second = await SeedAsync(connection);
        var own = await SeedDigitalResourceAsync(first);
        var foreign = await SeedDigitalResourceAsync(second);
        var storage = DigitalStorage(own);
        using var server = PrivateSqlServer(first.Options, storage);
        using var client = server.CreateClient();

        using (var allowed = await PrivateGetAsync(client, own.Reference,
            "Student", first.TenantId, first.UserId))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(own.Bytes, await allowed.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", allowed.Content.Headers.ContentType?.MediaType);
            AssertPrivateCache(allowed);
            Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
        }

        foreach (var (id, role, tenant, user, expected) in new[]
        {
            (foreign.Reference, "Student", first.TenantId, first.UserId, HttpStatusCode.NotFound),
            (own.Reference, "Student", second.TenantId, second.UserId, HttpStatusCode.NotFound),
            (Guid.NewGuid(), "Student", first.TenantId, first.UserId, HttpStatusCode.NotFound),
            (own.Reference, "Guardian", first.TenantId, first.UserId, HttpStatusCode.Forbidden)
        })
        {
            using var denied = await PrivateGetAsync(client, id, role, tenant, user);
            Assert.Equal(expected, denied.StatusCode);
            AssertPrivateCache(denied);
            Assert.NotEqual(own.Bytes, await denied.Content.ReadAsByteArrayAsync());
        }
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);

        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == first.Enrollments[0]);
            var campus = await db.Campuses.SingleAsync(x => x.Id == enrollment.CampusId);
            campus.IsActive = false;
            await db.SaveChangesAsync();
        }
        using (var campusDenied = await PrivateGetAsync(client, own.Reference,
            "Student", first.TenantId, first.UserId))
        {
            Assert.Equal(HttpStatusCode.NotFound, campusDenied.StatusCode);
            AssertPrivateCache(campusDenied);
        }
        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == first.Enrollments[0]);
            var campus = await db.Campuses.SingleAsync(x => x.Id == enrollment.CampusId);
            campus.IsActive = true;
            enrollment.IsCurrent = false;
            enrollment.State = EduOS.Core.Enums.Domain.EnrollmentState.Withdrawn;
            await db.SaveChangesAsync();
        }
        using var withdrawn = await PrivateGetAsync(client, own.Reference,
            "Student", first.TenantId, first.UserId);
        Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
        AssertPrivateCache(withdrawn);
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SQL_HTTP_storage_time_revocation_never_releases_private_bytes(bool documentRevoked)
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var asset = await SeedDigitalResourceAsync(fixture);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(asset.Key)).Returns(() => Task.Run(async () =>
        {
            // Keep the original HTTP request's AsyncLocal tenant intact while
            // a separate SQL connection commits the concurrent revocation.
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            if (documentRevoked)
                (await db.Documents.SingleAsync(x => x.PublicId == asset.Reference)).IsActive = false;
            else
            {
                var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                    x.PublicId == fixture.Enrollments[0]);
                enrollment.IsCurrent = false;
                enrollment.State = EduOS.Core.Enums.Domain.EnrollmentState.Withdrawn;
            }
            await db.SaveChangesAsync();
            return new FileDownloadResult { Content = asset.Bytes, ContentType = "application/pdf" };
        }));
        using var server = PrivateSqlServer(fixture.Options, storage);
        using var client = server.CreateClient();
        using var response = await PrivateGetAsync(client, asset.Reference,
            "Student", fixture.TenantId, fixture.UserId);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertPrivateCache(response);
        Assert.NotEqual(asset.Bytes, await response.Content.ReadAsByteArrayAsync());
        storage.Verify(x => x.GetPrivateFileAsync(asset.Key), Times.Once);
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(0, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId &&
            x.Action == "LibraryDigitalResource.Download"));
    }
}
