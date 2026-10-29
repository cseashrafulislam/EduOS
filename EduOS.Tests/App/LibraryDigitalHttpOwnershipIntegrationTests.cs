using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
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
/// HTTP TestServer with the REAL digital library service and tenant-filtered EF context.
/// InMemory persistence exercises routing, membership and direct-ID authorization;
/// the separate SQL Server suite verifies actual SQL locks and transactions.
/// </summary>
public sealed class LibraryDigitalHttpOwnershipIntegrationTests
{
    private sealed class TestAuthenticationHandler(
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
                !long.TryParse(tenant, out var tenantId) || tenantId <= 0 ||
                !long.TryParse(user, out var userId) || userId <= 0)
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

    private sealed class RequestUser(IHttpContextAccessor accessor) : ICurrentUserService
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public long UserId => long.TryParse(
            Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        public long TenantId => long.TryParse(
            Principal?.FindFirstValue("TenantId"), out var id) ? id : 0;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => IsInRole("SuperAdmin");
        public bool IsTenantAdmin => IsInRole("TenantAdmin");
        public IReadOnlyList<string> Roles => Principal?.FindAll(ClaimTypes.Role)
            .Select(x => x.Value).ToArray() ?? Array.Empty<string>();
        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static TestServer Server(Mock<IFileUploadService> storage)
    {
        var module = new Mock<ITenantModuleService>();
        module.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        var databaseName = "library-digital-http-" + Guid.NewGuid().ToString("N");
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("LibraryHttpTest")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        "LibraryHttpTest", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(module.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(options =>
                    options.AddFixedWindowLimiter("ApiPolicy", limiter =>
                    {
                        limiter.PermitLimit = 1000;
                        limiter.Window = TimeSpan.FromMinutes(1);
                        limiter.QueueLimit = 0;
                    }));
                services.AddDbContext<EduOSDbContext>(options =>
                    options.UseInMemoryDatabase(databaseName));
                services.AddScoped<ICurrentUserService, RequestUser>();
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
                app.UseRateLimiter();
                app.UseAuthentication();
                // Only the test handler resolves a validated identity to the ambient
                // tenant scope. Never accept an unverified header as production scope.
                app.Use(async (context, next) =>
                {
                    if (context.User.Identity?.IsAuthenticated == true &&
                        long.TryParse(context.User.FindFirstValue("TenantId"), out var tenantId))
                        context.Items["TenantId"] = tenantId;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    private static async Task<Guid> SeedAsync(TestServer server, long tenant, long user,
        Mock<IFileUploadService> storage)
    {
        using var scope = server.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, "TenantAdmin")
            }, "Seed"))
        };
        http.Items["TenantId"] = tenant;
        accessor.HttpContext = http;
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var campus = new Campus { TenantId = tenant, Name = "Campus", Code = "C",
            IsActive = true };
        var book = new Book { TenantId = tenant, Title = "Restricted book", IsActive = true };
        var student = new Student { TenantId = tenant, PersonId = user + 100,
            UserId = user, StudentCode = "S-" + user, FullName = "Test reader",
            StatusCode = "Active" };
        db.AddRange(campus, book, student);
        await db.SaveChangesAsync();
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            TenantId = tenant, StudentId = student.Id, CampusId = campus.Id,
            State = EnrollmentState.Active, IsCurrent = true,
            RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1)
        });
        var reference = Guid.NewGuid();
        var key = "tenant-" + tenant + "/documents/" + reference.ToString("N") + ".pdf";
        var bytes = "%PDF-1.4\nHTTP private fixture"u8.ToArray();
        var file = new FileAsset
        {
            TenantId = tenant, PublicId = reference, StorageProvider = "LocalPrivate",
            StorageKey = key, OriginalFileName = "private.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            UploadedByUserId = user, MalwareScanStateCode = "Clean",
            IsVerifiedSafe = true, Visibility = FileVisibility.Private
        };
        db.FileAssets.Add(file);
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        {
            TenantId = tenant, Code = "LIBRARY_DIGITAL", Name = "Digital",
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.Documents.Add(new Document
        {
            TenantId = tenant, PublicId = reference, FileAssetId = file.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = book.Id, Title = "Private resource", IsActive = true
        });
        await db.SaveChangesAsync();
        storage.Setup(x => x.GetPrivateFileAsync(key)).ReturnsAsync(
            new FileDownloadResult { Content = bytes, ContentType = "application/pdf" });
        accessor.HttpContext = null;
        return reference;
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string url, string role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Http_real_service_denies_cross_tenant_guessed_id_and_ungranted_principal()
    {
        var storage = new Mock<IFileUploadService>();
        using var server = Server(storage);
        var first = await SeedAsync(server, 101, 7, storage);
        var second = await SeedAsync(server, 202, 8, storage);
        using var client = server.CreateClient();

        using var own = await GetAsync(client,
            "/api/library/digital/" + first + "/download", "Student", 101, 7);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("application/pdf", own.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", own.Headers.CacheControl?.ToString() ?? "");

        using var foreign = await GetAsync(client,
            "/api/library/digital/" + second + "/download", "Student", 101, 7);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var switched = await GetAsync(client,
            "/api/library/digital/" + first + "/download", "Student", 202, 8);
        Assert.Equal(HttpStatusCode.NotFound, switched.StatusCode);
        using var guessed = await GetAsync(client,
            "/api/library/digital/" + Guid.NewGuid() + "/download", "Student", 101, 7);
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
        using var principal = await GetAsync(client,
            "/api/library/digital/" + first + "/download", "Principal", 101, 7);
        Assert.Equal(HttpStatusCode.NotFound, principal.StatusCode);
        using var principalList = await GetAsync(client,
            "/api/library/digital", "Principal", 101, 7);
        Assert.Equal(HttpStatusCode.Forbidden, principalList.StatusCode);
        using var studentExport = await GetAsync(client,
            "/api/library/digital/export", "Student", 101, 7);
        Assert.Equal(HttpStatusCode.Forbidden, studentExport.StatusCode);

        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);
        using var firstScope = server.Services.CreateScope();
        var context = firstScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        var accessor = firstScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Items["TenantId"] = 101L;
        Assert.Equal(1, await context.AuditLogs.CountAsync(x =>
            x.TenantId == 101 && x.Action == "LibraryDigitalResource.Download"));
    }
}
