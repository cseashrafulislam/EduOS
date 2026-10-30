using System.Net;
using System.Net.Http;
using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Dashboard;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL entitlement -> MVC Dashboard -> production shared Razor layout -> localized
/// V2 role links. The dashboard data is inert, but the rendered shell, authorization
/// identity, SQL module service and translations are real. This is not browser UAT.
/// </summary>
public sealed partial class SqlServerV2SixteenInstituteEntitlementHttpAcceptanceTests
{
    private static TestServer RolePortalRazorServer(DbContextOptions<EduOSDbContext> options)
    {
        var dashboard = new Mock<IDashboardService>();
        dashboard.Setup(x => x.GetDashboardAsync())
            .ReturnsAsync(ApiResponse<DashboardVm>.SuccessResponse(new DashboardVm
            {
                InstitutionName = "SQL role portal acceptance",
                PlanCode = "PRO",
                PlanName = "PRO",
                OnboardingComplete = true
            }));

        var contentRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "EduOS.App"));

        return new TestServer(new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddLocalization(x => x.ResourcesPath = "Resources");
                services.Configure<RequestLocalizationOptions>(x =>
                {
                    x.SetDefaultCulture("en-BD");
                    x.AddSupportedCultures("en-BD", "bn-BD");
                    x.AddSupportedUICultures("en-BD", "bn-BD");
                });
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("SqlMatrix")
                    .AddScheme<AuthenticationSchemeOptions, SqlMatrixAuth>("SqlMatrix", _ => { });
                services.AddAuthorization();
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, SqlMatrixUser>();
                services.AddScoped<ITenantModuleService>(sp => RealModules(
                    sp.GetRequiredService<EduOSDbContext>(),
                    sp.GetRequiredService<ICurrentUserService>()));
                services.AddSingleton(dashboard.Object);
                services.AddAntiforgery();
                services.AddControllersWithViews()
                    .AddViewLocalization()
                    .AddApplicationPart(typeof(DashboardController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRequestLocalization();
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
                app.UseEndpoints(endpoints =>
                    endpoints.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}"));
            }));
    }

    private static async Task<HttpResponseMessage> RolePortalRazorGetAsync(
        HttpClient client, long? tenant, string? role, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Dashboard/Index");
        if (tenant.HasValue)
            request.Headers.TryAddWithoutValidation("X-Matrix-Tenant", tenant.Value.ToString());
        if (role is not null)
            request.Headers.TryAddWithoutValidation("X-Matrix-Role", role);
        request.Headers.AcceptLanguage.ParseAdd(culture);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Sixteen_SQL_institutes_render_real_bilingual_Razor_role_navigation_without_tenant_bleed()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var institutes = await SeedAsync(options);
        using var server = RolePortalRazorServer(options);
        using var client = server.CreateClient();

        var allPaths = new[]
        {
            "/Learning/CourseCompletions", "/Learning/MySubmissions",
            "/Library/Circulation", "/Library/Digital",
            "/hostel/guardian-stays/print", "/hostel/my-history/print",
            "/TransportOperations/Family", "/TransportOperations/Trips",
            "/TransportOperations/DriverTrips"
        };
        var roles = new[] { "Student", "Teacher", "Librarian", "Guardian",
            "Parent", "TransportManager", "Staff", "TenantAdmin" };

        // Same HTTP client, two languages and multiple roles, then reverse tenant
        // order. The SQL entitlement service must resolve the current tenant anew.
        foreach (var (institute, index) in institutes.Select((item, i) => (item, i))
                     .Concat(institutes.AsEnumerable().Reverse().Select((item, i) => (item, i + 16))))
        {
            var role = roles[index % roles.Length];
            var culture = index % 2 == 0 ? "bn-BD" : "en-BD";
            using var response = await RolePortalRazorGetAsync(
                client, institute.TenantId, role, culture);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("name=\"viewport\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("id=\"sidebarMenu\"", html, StringComparison.OrdinalIgnoreCase);

            var available = new HashSet<string>(
                new[] { "LMS", "LIBRARY", "HOSTEL", "TRANSPORT" }
                    .Where(institute.Configuration.Available),
                StringComparer.OrdinalIgnoreCase);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role)], "SqlMatrix"));
            var expectedPaths = V2RolePortalNavigation.GetLinks(available, principal)
                .Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var path in allPaths)
                Assert.Equal(expectedPaths.Contains(path),
                    html.Contains($"href=\"{path}\"", StringComparison.OrdinalIgnoreCase));

            if (culture == "bn-BD" && expectedPaths.Contains("/Learning/MySubmissions"))
                Assert.Contains("আমার জমা দেওয়া কাজ", html);
            if (culture == "bn-BD" && expectedPaths.Contains("/TransportOperations/Family"))
                Assert.Contains("পরিবারের পরিবহন সূচি", html);
        }

        // Anonymous identity must not receive the tenant's private role shortcuts.
        using var anonymous = await RolePortalRazorGetAsync(
            client, institutes[0].TenantId, null, "bn-BD");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
