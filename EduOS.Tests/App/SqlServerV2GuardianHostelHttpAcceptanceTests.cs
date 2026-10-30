using System.Net;
using System.Net.Http;
using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.App.Middleware;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Hostel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Moq;

namespace EduOS.Tests.App;

/// <summary>
/// SQL-backed HTTP acceptance for the real Guardian/Parent Hostel read, CSV and Razor
/// print surfaces. Uses only disposable test tenants and the production service query;
/// it does not substitute mocked service data or touch customer storage.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record GuardianHostelFixture(
        string VisibleCode, string UnconsentedCode, long LinkId, long CampusId,
        long HostelId, long RoomId, long BedId, long EnrollmentId);

    private static TestServer GuardianHostelHttpServer(
        DbContextOptions<EduOSDbContext> options, bool moduleEnabled = true)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(moduleEnabled);
        var contentRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "EduOS.App"));
        return new TestServer(new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddLocalization(x => x.ResourcesPath = "Resources");
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("SqlPrivateTest")
                    .AddScheme<AuthenticationSchemeOptions, PrivateHttpAuth>(
                        "SqlPrivateTest", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(modules.Object);
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, PrivateHttpUser>();
                services.AddScoped<IGuardianHostelStayService, GuardianHostelStayService>();
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddViewLocalization()
                    .AddApplicationPart(typeof(GuardianHostelStaysPrintController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseV2PrivateApiResponseHeaders();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.Use(async (http, next) =>
                {
                    if (http.User.Identity?.IsAuthenticated == true &&
                        long.TryParse(http.User.FindFirstValue("TenantId"), out var tenant))
                        http.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapControllerRoute("guardian-hostel-print",
                        "{controller=Home}/{action=Index}/{id?}");
                });
            }));
    }

    private static readonly string[] GuardianHostelRoutes =
    [
        "/api/hostel/guardian-stays?page=1&pageSize=20",
        "/api/hostel/guardian-stays/export?page=1",
        "/hostel/guardian-stays/print?page=1"
    ];

    private static async Task<HttpResponseMessage> GuardianHostelGetAsync(
        HttpClient client, string path, string? role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    private static async Task AssertGuardianHostelRoutesAsync(
        HttpClient client, string role, long tenant, long user,
        string visibleCode, string unconsentedCode, string foreignCode, bool visible)
    {
        foreach (var path in GuardianHostelRoutes)
        {
            using var response = await GuardianHostelGetAsync(client, path, role, tenant, user);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            var body = await response.Content.ReadAsStringAsync();
            if (visible)
                Assert.Contains(visibleCode, body);
            else
                Assert.DoesNotContain(visibleCode, body);
            Assert.DoesNotContain(unconsentedCode, body);
            Assert.DoesNotContain(foreignCode, body);
            if (path.Contains("/print", StringComparison.Ordinal))
            {
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains("noindex,nofollow", body);
            }
            if (path.Contains("/export", StringComparison.Ordinal))
                Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? "");
        }
    }

    private static async Task<GuardianHostelFixture> SeedGuardianHostelStaysAsync(
        Fixture fixture, long[] beds)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var enrollments = await db.StudentEnrollments
            .Where(x => x.TenantId == fixture.TenantId &&
                fixture.Enrollments.Contains(x.PublicId))
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StudentId, x.CampusId })
            .ToArrayAsync();
        Assert.Equal(2, enrollments.Length);
        var first = await db.Students.SingleAsync(x => x.Id == enrollments[0].StudentId);
        var second = await db.Students.SingleAsync(x => x.Id == enrollments[1].StudentId);
        var bed = await db.HostelBeds.SingleAsync(x => x.Id == beds[0]);
        var room = await db.HostelRooms.SingleAsync(x => x.Id == bed.HostelRoomId);
        var hostel = await db.Hostels.SingleAsync(x => x.Id == room.HostelId);
        var person = new Person { FullName = "SQL guardian for protected Hostel readback" };
        db.Add(person);
        await db.SaveChangesAsync();
        var guardian = new Guardian
        {
            TenantId = fixture.TenantId, PersonId = person.Id, UserId = fixture.UserId,
            FullName = "SQL guardian", IsActive = true
        };
        db.Guardians.Add(guardian);
        await db.SaveChangesAsync();
        var allowed = new StudentGuardian
        {
            TenantId = fixture.TenantId, StudentId = first.Id,
            GuardianId = guardian.Id, RelationCode = "Parent",
            IsAuthorizedPickup = true
        };
        var noPickup = new StudentGuardian
        {
            TenantId = fixture.TenantId, StudentId = second.Id,
            GuardianId = guardian.Id, RelationCode = "Parent",
            IsAuthorizedPickup = false
        };
        db.StudentGuardians.AddRange(allowed, noPickup);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        first.AdmissionDate = today.AddDays(-30);
        second.AdmissionDate = today.AddDays(-30);
        db.StudentHostelAllocations.Add(new StudentHostelAllocation
        {
            TenantId = fixture.TenantId, StudentId = first.Id,
            StudentEnrollmentId = enrollments[0].Id, HostelBedId = beds[0],
            ClientRequestId = Guid.NewGuid(), StartDate = today.AddDays(-1),
            EndDate = today.AddDays(1), State = HostelAllocationState.Active
        });
        if (beds.Length > 1)
            db.StudentHostelAllocations.Add(new StudentHostelAllocation
            {
                TenantId = fixture.TenantId, StudentId = second.Id,
                StudentEnrollmentId = enrollments[1].Id, HostelBedId = beds[1],
                ClientRequestId = Guid.NewGuid(), StartDate = today.AddDays(-1),
                EndDate = today.AddDays(1), State = HostelAllocationState.Active
            });
        await db.SaveChangesAsync();
        return new GuardianHostelFixture(first.StudentCode, second.StudentCode,
            allowed.Id, enrollments[0].CampusId, hostel.Id, room.Id, bed.Id,
            enrollments[0].Id);
    }

    [Fact]
    public async Task SQL_HTTP_guardian_hostel_print_api_and_csv_follow_live_consent_campus_and_enrollment()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var home = await SeedAsync(connection);
        var foreign = await SeedAsync(connection);
        var homeData = await SeedGuardianHostelStaysAsync(home,
            await SeedHostelBedsAsync(home, 2, 2));
        var foreignData = await SeedGuardianHostelStaysAsync(foreign,
            await SeedHostelBedsAsync(foreign, 1, 1));

        using var server = GuardianHostelHttpServer(home.Options);
        using var client = server.CreateClient();

        foreach (var role in new[] { "Guardian", "Parent" })
            await AssertGuardianHostelRoutesAsync(client, role, home.TenantId, home.UserId,
                homeData.VisibleCode, homeData.UnconsentedCode, foreignData.VisibleCode, true);

        // One browser/client switching tenants must never reuse previous private rows.
        await AssertGuardianHostelRoutesAsync(client, "Guardian", foreign.TenantId, foreign.UserId,
            foreignData.VisibleCode, foreignData.UnconsentedCode, homeData.VisibleCode, true);
        await AssertGuardianHostelRoutesAsync(client, "Parent", home.TenantId, home.UserId,
            homeData.VisibleCode, homeData.UnconsentedCode, foreignData.VisibleCode, true);
        await AssertGuardianHostelRoutesAsync(client, "Guardian", foreign.TenantId, home.UserId,
            foreignData.VisibleCode, foreignData.UnconsentedCode, homeData.VisibleCode, false);

        foreach (var role in new string?[] { null, "Student", "HostelWarden" })
        {
            foreach (var path in GuardianHostelRoutes)
            {
                using var denied = await GuardianHostelGetAsync(client, path,
                    role, home.TenantId, home.UserId);
                Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                    denied.StatusCode);
                Assert.Contains("no-store", denied.Headers.CacheControl?.ToString() ?? "",
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(homeData.VisibleCode,
                    await denied.Content.ReadAsStringAsync());
            }
        }

        async Task AssertHomeHiddenAsync() =>
            await AssertGuardianHostelRoutesAsync(client, "Guardian", home.TenantId,
                home.UserId, homeData.VisibleCode, homeData.UnconsentedCode,
                foreignData.VisibleCode, false);
        async Task AssertHomeVisibleAsync() =>
            await AssertGuardianHostelRoutesAsync(client, "Parent", home.TenantId,
                home.UserId, homeData.VisibleCode, homeData.UnconsentedCode,
                foreignData.VisibleCode, true);

        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x => x.Id == homeData.LinkId))
                .IsAuthorizedPickup = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x => x.Id == homeData.LinkId))
                .IsAuthorizedPickup = true;
            (await db.Campuses.SingleAsync(x => x.Id == homeData.CampusId))
                .IsActive = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.Campuses.SingleAsync(x => x.Id == homeData.CampusId))
                .IsActive = true;
            (await db.HostelBeds.SingleAsync(x => x.Id == homeData.BedId))
                .IsActive = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.HostelBeds.SingleAsync(x => x.Id == homeData.BedId))
                .IsActive = true;
            (await db.StudentEnrollments.SingleAsync(x => x.Id == homeData.EnrollmentId))
                .IsCurrent = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.StudentEnrollments.SingleAsync(x => x.Id == homeData.EnrollmentId))
                .IsCurrent = true;
            (await db.HostelRooms.SingleAsync(x => x.Id == homeData.RoomId))
                .IsActive = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.HostelRooms.SingleAsync(x => x.Id == homeData.RoomId))
                .IsActive = true;
            (await db.Hostels.SingleAsync(x => x.Id == homeData.HostelId))
                .IsActive = false;
            await db.SaveChangesAsync();
        }
        await AssertHomeHiddenAsync();
        await using (var db = ScopedContext(home.Options, home.TenantId, home.UserId))
        {
            (await db.Hostels.SingleAsync(x => x.Id == homeData.HostelId))
                .IsActive = true;
            await db.SaveChangesAsync();
        }
        await AssertHomeVisibleAsync();

        using var gatedServer = GuardianHostelHttpServer(home.Options, moduleEnabled: false);
        using var gatedClient = gatedServer.CreateClient();
        foreach (var path in GuardianHostelRoutes)
        {
            using var gated = await GuardianHostelGetAsync(gatedClient, path,
                "Guardian", home.TenantId, home.UserId);
            Assert.Equal(HttpStatusCode.Forbidden, gated.StatusCode);
            Assert.Contains("no-store", gated.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(homeData.VisibleCode,
                await gated.Content.ReadAsStringAsync());
        }
    }
}
