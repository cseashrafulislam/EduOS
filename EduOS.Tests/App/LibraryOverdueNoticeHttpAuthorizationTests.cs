using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Antiforgery;
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
/// Real controller, authorization policy, antiforgery filter and Library service
/// over HTTP. InMemory validates tenant/role/campus behavior, not SQL locking;
/// separate SQL integration tests cover concurrent sends and idempotency.
/// </summary>
public sealed class LibraryOverdueNoticeHttpAuthorizationTests
{
    private sealed class HeaderAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!long.TryParse(Request.Headers["X-Test-Tenant"], out var tenant) || tenant <= 0 ||
                !long.TryParse(Request.Headers["X-Test-User"], out var user) || user <= 0 ||
                string.IsNullOrWhiteSpace(Request.Headers["X-Test-Role"]))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString()),
                new Claim("TenantId", tenant.ToString())
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
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

    private static TestServer Server(bool moduleEnabled = true)
    {
        var module = new Mock<ITenantModuleService>();
        module.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(moduleEnabled);
        var database = "library-notice-http-" + Guid.NewGuid().ToString("N");
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("NoticeHttpTest")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(
                        "NoticeHttpTest", _ => { });
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
                    options.UseInMemoryDatabase(database));
                services.AddScoped<ICurrentUserService, RequestUser>();
                services.AddScoped<ILibraryOverdueNoticeService, LibraryOverdueNoticeService>();
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(LibraryOverdueNoticesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.Use(async (context, next) =>
                {
                    if (context.User.Identity?.IsAuthenticated == true &&
                        long.TryParse(context.User.FindFirstValue("TenantId"), out var tenant))
                        context.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/__test/notice-antiforgery", async context =>
                    {
                        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                        var token = antiforgery.GetAndStoreTokens(context);
                        await context.Response.WriteAsync(token.RequestToken ?? "");
                    }).RequireAuthorization();
                    endpoints.MapControllers();
                });
            }));
    }

    private static async Task<long> SeedAsync(TestServer server, long tenant, long actor)
    {
        using var scope = server.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "Seed"))
        };
        http.Items["TenantId"] = tenant;
        accessor.HttpContext = http;
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
            var campus = new Campus { TenantId = tenant, Name = "Campus", Code = "C",
                IsActive = true };
            var book = new Book { TenantId = tenant, Title = "Overdue title", IsActive = true };
            var student = new Student { TenantId = tenant, PersonId = tenant + 1000,
                UserId = actor + 10, StudentCode = "S-" + tenant,
                FullName = "Reader", StatusCode = "Active" };
            db.AddRange(campus, book, student);
            await db.SaveChangesAsync();
            var branch = new LibraryBranch
            {
                TenantId = tenant, CampusId = campus.Id,
                Name = "Campus Library", Code = "L", IsActive = true
            };
            db.LibraryBranches.Add(branch);
            db.StudentEnrollments.Add(new StudentEnrollment
            {
                TenantId = tenant, StudentId = student.Id, CampusId = campus.Id,
                State = EnrollmentState.Active, IsCurrent = true,
                RollNo = "1", EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40)
            });
            await db.SaveChangesAsync();
            var copy = new BookCopy
            {
                TenantId = tenant, BookId = book.Id, LibraryBranchId = branch.Id,
                AccessionNumber = "A-" + tenant, State = BookCopyState.Issued
            };
            db.BookCopies.Add(copy);
            await db.SaveChangesAsync();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.BookIssues.Add(new BookIssue
            {
                TenantId = tenant, BookCopyId = copy.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), IssueDate = today.AddDays(-20),
                DueDate = today.AddDays(-2), State = BookIssueState.Issued
            });
            await db.SaveChangesAsync();
            return campus.Id;
        }
        finally { accessor.HttpContext = null; }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url,
        string role, long tenant, long user)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return request;
    }

    [Fact]
    public async Task Http_preview_rejects_foreign_campus_student_and_revoked_principal_grant()
    {
        using var server = Server();
        var ownCampus = await SeedAsync(server, 101, 7);
        var foreignCampus = await SeedAsync(server, 202, 8);
        using var client = server.CreateClient();

        async Task<HttpResponseMessage> Get(string role, long tenant, long user, long campus)
        {
            using var request = Request(HttpMethod.Get,
                "/api/library/overdue-notices/preview?campusId=" + campus,
                role, tenant, user);
            return await client.SendAsync(request);
        }

        using var own = await Get("TenantAdmin", 101, 7, ownCampus);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("no-store", own.Headers.CacheControl?.ToString() ?? "");
        Assert.Contains("S-101", await own.Content.ReadAsStringAsync());
        using var foreign = await Get("TenantAdmin", 101, 7, foreignCampus);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var switched = await Get("TenantAdmin", 202, 8, ownCampus);
        Assert.Equal(HttpStatusCode.Forbidden, switched.StatusCode);
        using var guessed = await Get("TenantAdmin", 101, 7, long.MaxValue);
        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        using var student = await Get("Student", 101, 7, ownCampus);
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
        using var principal = await Get("Principal", 101, 7, ownCampus);
        Assert.Equal(HttpStatusCode.Forbidden, principal.StatusCode);

        using (var scope = server.Services.CreateScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.Items["TenantId"] = 101L;
            var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
            db.UserCampusAccesses.Add(new UserCampusAccess
            {
                TenantId = 101, CampusId = ownCampus, UserId = 7, IsActive = true
            });
            await db.SaveChangesAsync();
            accessor.HttpContext = null;
        }
        using var granted = await Get("Principal", 101, 7, ownCampus);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using (var scope = server.Services.CreateScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.Items["TenantId"] = 101L;
            var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
            var grant = await db.UserCampusAccesses.SingleAsync();
            grant.IsActive = false;
            await db.SaveChangesAsync();
            accessor.HttpContext = null;
        }
        using var revoked = await Get("Principal", 101, 7, ownCampus);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task Http_send_requires_antiforgery_and_replays_one_authorized_batch()
    {
        using var server = Server();
        var campus = await SeedAsync(server, 101, 7);
        using var client = server.CreateClient();
        var dto = new SendLibraryOverdueNoticesRequestDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campus,
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        using (var missing = Request(HttpMethod.Post, "/api/library/overdue-notices/send",
                   "TenantAdmin", 101, 7))
        {
            missing.Content = JsonContent.Create(dto);
            using var denied = await client.SendAsync(missing);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using var tokenRequest = Request(HttpMethod.Get,
            "/__test/notice-antiforgery", "TenantAdmin", 101, 7);
        using var tokenResponse = await client.SendAsync(tokenRequest);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadAsStringAsync();
        var cookie = tokenResponse.Headers.GetValues("Set-Cookie")
            .Select(x => x.Split(';')[0]).Aggregate((a, b) => a + "; " + b);

        async Task<HttpResponseMessage> Send(string role, long tenant, long user)
        {
            using var request = Request(HttpMethod.Post,
                "/api/library/overdue-notices/send", role, tenant, user);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("RequestVerificationToken", token);
            request.Content = JsonContent.Create(dto);
            return await client.SendAsync(request);
        }

        using var first = await Send("TenantAdmin", 101, 7);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("\"sentCount\":1", await first.Content.ReadAsStringAsync());
        using var replay = await Send("TenantAdmin", 101, 7);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("\"alreadyProcessed\":true", await replay.Content.ReadAsStringAsync());
        using var wrongRole = await Send("Student", 101, 7);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        using var wrongTenant = await Send("TenantAdmin", 202, 8);
        Assert.NotEqual(HttpStatusCode.OK, wrongTenant.StatusCode);

        using var scope = server.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        accessor.HttpContext.Items["TenantId"] = 101L;
        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync(x =>
            x.TenantId == 101 && x.TypeCode == "LIBRARY_OVERDUE"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x =>
            x.TenantId == 101 && x.Action == "LibraryOverdueNotice.Send"));
    }

    [Fact]
    public async Task Http_disabled_library_entitlement_denies_preview()
    {
        using var server = Server(moduleEnabled: false);
        var campus = await SeedAsync(server, 101, 7);
        using var client = server.CreateClient();
        using var request = Request(HttpMethod.Get,
            "/api/library/overdue-notices/preview?campusId=" + campus,
            "TenantAdmin", 101, 7);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
