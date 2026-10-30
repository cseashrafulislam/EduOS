using System.Net;
using System.Net.Http;
using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Middleware;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
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
/// Real Razor HTTP + SQL authorization for the LMS learner/instructor print route.
/// Runs against disposable migrated SQL fixtures; no customer data or PDF rendering
/// is assumed. Browser print and mobile PDF remain separate acceptance gates.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static TestServer LearnerCertificateSqlPrintServer(
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
                services.AddScoped<ILmsWorkflowService>(sp =>
                {
                    var actor = sp.GetRequiredService<ICurrentUserService>();
                    return SqlCertificateWorkflow(sp.GetRequiredService<EduOSDbContext>(),
                        actor.TenantId, actor.UserId, actor.Roles.FirstOrDefault() ?? "");
                });
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddViewLocalization()
                    .AddApplicationPart(typeof(LearningController).Assembly);
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
                    endpoints.MapControllerRoute("learner-print", "{controller=Home}/{action=Index}/{id?}"));
            }));
    }

    private static async Task<HttpResponseMessage> LearnerPrintGetAsync(
        HttpClient client, Guid reference, string? role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/Learning/CertificatePrint?reference={reference:D}");
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    private static async Task AssertLearnerCertificateSqlPrintAsync(
        HttpClient client, long tenant, long user, Guid reference, bool visible)
    {
        foreach (var role in new[] { "Student", "Principal" })
        {
            using var response = await LearnerPrintGetAsync(client, reference, role, tenant, user);
            Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            var html = await response.Content.ReadAsStringAsync();
            if (visible)
            {
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains(reference.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Original SQL course title", html);
                Assert.Contains("frame-ancestors 'none'",
                    response.Headers.GetValues("Content-Security-Policy").Single());
                Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
                Assert.Contains("noindex,nofollow,noarchive", html);
            }
            else
            {
                Assert.DoesNotContain("Original SQL course title", html);
                Assert.DoesNotContain(reference.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (!visible) return;
        foreach (var (role, scopedTenant, scopedUser, id, expected) in new[]
        {
            ((string?)null, tenant, user, reference, HttpStatusCode.Unauthorized),
            ("Guardian", tenant, user, reference, HttpStatusCode.Forbidden),
            ("Student", tenant + 999, user, reference, HttpStatusCode.NotFound),
            ("Student", tenant, user + 999, reference, HttpStatusCode.NotFound),
            ("Student", tenant, user, Guid.NewGuid(), HttpStatusCode.NotFound)
        })
        {
            using var denied = await LearnerPrintGetAsync(client, id, role, scopedTenant, scopedUser);
            Assert.Equal(expected, denied.StatusCode);
            Assert.Contains("no-store", denied.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Original SQL course title",
                await denied.Content.ReadAsStringAsync());
        }
    }
}
