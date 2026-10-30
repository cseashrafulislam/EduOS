using System.Net;
using System.Net.Http;
using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Middleware;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// SQL-backed HTTP acceptance of the real Parent/Guardian Razor print route.
/// Reuses the existing disposable SQL certificate/consent fixture, and exercises
/// consent revocation, regrant, relationship revocation and direct-ID isolation.
/// No customer records or live storage are accessed.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static TestServer GuardianCertificateSqlPrintServer(
        DbContextOptions<EduOSDbContext> options)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        var contentRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "EduOS.App"));
        return new TestServer(new WebHostBuilder()
            .UseContentRoot(contentRoot)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                // The production Razor _ViewImports injects IStringLocalizer<SharedResource>.
                // Match production MVC localization rather than bypassing the real view.
                services.AddLocalization(options => options.ResourcesPath = "Resources");
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
                services.AddScoped<ILmsCertificateGuardianService, LmsCertificateGuardianService>();
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddViewLocalization()
                    .AddApplicationPart(typeof(LmsGuardianCertificatesController).Assembly);
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
                    endpoints.MapControllerRoute("guardian-print", "{controller=Home}/{action=Index}/{id?}"));
            }));
    }

    private static async Task<HttpResponseMessage> GuardianPrintGetAsync(
        HttpClient client, Guid reference, string? role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/LmsGuardianCertificates/Print?reference={reference:D}");
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    private static async Task AssertGuardianCertificateSqlPrintAsync(
        HttpClient client, long tenant, long user, Guid reference, bool visible)
    {
        foreach (var role in new[] { "Guardian", "Parent" })
        {
            using var response = await GuardianPrintGetAsync(client, reference, role, tenant, user);
            Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            var html = await response.Content.ReadAsStringAsync();
            if (visible)
            {
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains(reference.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("SQL completion", html);
                Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
                Assert.Contains("frame-ancestors 'none'",
                    response.Headers.GetValues("Content-Security-Policy").Single());
            }
            else
            {
                Assert.DoesNotContain("SQL completion", html);
                Assert.DoesNotContain(reference.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!visible) return;
        foreach (var (role, scopedTenant, scopedUser, id, expected) in new[]
        {
            ((string?)null, tenant, user, reference, HttpStatusCode.Unauthorized),
            ("Student", tenant, user, reference, HttpStatusCode.Forbidden),
            ("Guardian", tenant + 999, user, reference, HttpStatusCode.NotFound),
            ("Guardian", tenant, user + 999, reference, HttpStatusCode.NotFound),
            ("Guardian", tenant, user, Guid.NewGuid(), HttpStatusCode.NotFound)
        })
        {
            using var response = await GuardianPrintGetAsync(client, id, role, scopedTenant, scopedUser);
            Assert.Equal(expected, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SQL completion", body);
        }
    }
}
