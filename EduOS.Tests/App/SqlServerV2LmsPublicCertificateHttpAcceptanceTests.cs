using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using EduOS.App.Middleware;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real HTTP routing, authorization, CSRF, rate limiting and SQL persistence for
/// the learner-consented public LMS certificate workflow. No customer database.
/// Uses the same disposable SQL fixture as the certificate race tests.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static TestServer PublicLmsHttpServer(DbContextOptions<EduOSDbContext> options)
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
                // Exercise production policies, including the anonymous 10/minute budget.
                services.AddRateLimiterConfiguration();
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, PrivateHttpUser>();
                services.AddScoped<ILmsPublicCertificateVerificationService,
                    LmsPublicCertificateVerificationService>();
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(PublicLmsCertificateVerificationController).Assembly);
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
                app.UseEndpoints(routes =>
                {
                    routes.MapControllers();
                    // Test-only token bootstrap; production uses the existing Razor CSRF meta.
                    routes.MapGet("/test/lms/antiforgery",
                        (HttpContext http, IAntiforgery antiforgery) =>
                            Results.Json(new
                            {
                                token = antiforgery.GetAndStoreTokens(http).RequestToken
                            }));
                });
            }));
    }

    private static async Task<HttpResponseMessage> PublicLmsRequestAsync(
        HttpClient client, HttpMethod method, string path, long? tenant = null,
        long? user = null, string? role = null, object? body = null,
        string? csrf = null, string? cookie = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (tenant.HasValue)
            request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.Value.ToString());
        if (user.HasValue)
            request.Headers.TryAddWithoutValidation("X-Test-User", user.Value.ToString());
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        if (csrf != null)
            request.Headers.TryAddWithoutValidation("RequestVerificationToken", csrf);
        if (cookie != null)
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> PublicLmsDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static void AssertPublicLmsNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);

    private static async Task AssertPublicLmsHttpAcceptanceAsync(
        DbContextOptions<EduOSDbContext> options, long tenant, long user, Guid certificate)
    {
        using var server = PublicLmsHttpServer(options);
        using var client = server.CreateClient();
        var route = $"/api/lms/certificate-verification/{certificate}";
        const string publicRoute = "/api/public/lms-certificate-verification";

        using (var anonymous = await PublicLmsRequestAsync(client, HttpMethod.Get, route))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            AssertPublicLmsNoStore(anonymous);
        }
        using (var guardian = await PublicLmsRequestAsync(
            client, HttpMethod.Get, route, tenant, user, "Guardian"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, guardian.StatusCode);
            AssertPublicLmsNoStore(guardian);
        }
        using (var foreign = await PublicLmsRequestAsync(
            client, HttpMethod.Get, route, tenant + 1000, user, "Student"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            AssertPublicLmsNoStore(foreign);
        }
        string version;
        using (var own = await PublicLmsRequestAsync(
            client, HttpMethod.Get, route, tenant, user, "Student"))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            version = (await PublicLmsDataAsync(own)).GetProperty("rowVersion").GetString()!;
            AssertPublicLmsNoStore(own);
        }
        using (var csrfMissing = await PublicLmsRequestAsync(
            client, HttpMethod.Post, route + "/enable", tenant, user, "Student",
            new { rowVersion = version }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, csrfMissing.StatusCode);
            AssertPublicLmsNoStore(csrfMissing);
        }
        string csrf;
        string cookie;
        using (var bootstrap = await PublicLmsRequestAsync(
            client, HttpMethod.Get, "/test/lms/antiforgery", tenant, user, "Student"))
        {
            Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
            csrf = (await PublicLmsDataAsyncBootstrap(bootstrap));
            cookie = bootstrap.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }
        string first;
        using (var enabled = await PublicLmsRequestAsync(
            client, HttpMethod.Post, route + "/enable", tenant, user, "Student",
            new { rowVersion = version }, csrf, cookie))
        {
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            var data = await PublicLmsDataAsync(enabled);
            first = data.GetProperty("token").GetString()!;
            version = data.GetProperty("rowVersion").GetString()!;
            Assert.Equal(43, first.Length);
            AssertPublicLmsNoStore(enabled);
        }
        using (var valid = await PublicLmsRequestAsync(
            client, HttpMethod.Post, publicRoute, body: new { token = first }))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            var json = await valid.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Private SQL learning course", json);
            Assert.DoesNotContain("Private Learner", json);
            Assert.DoesNotContain(first, json);
            Assert.Equal("Valid", (await PublicLmsDataAsync(valid))
                .GetProperty("status").GetString());
            AssertPublicLmsNoStore(valid);
        }
        using (var rotated = await PublicLmsRequestAsync(
            client, HttpMethod.Post, route + "/enable", tenant, user, "Student",
            new { rowVersion = version }, csrf, cookie))
        {
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var data = await PublicLmsDataAsync(rotated);
            version = data.GetProperty("rowVersion").GetString()!;
            var second = data.GetProperty("token").GetString()!;
            Assert.NotEqual(first, second);
            using var old = await PublicLmsRequestAsync(
                client, HttpMethod.Post, publicRoute, body: new { token = first });
            Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);

            // Live entitlement withdrawal must invalidate the bearer without
            // disclosing whether the token, institute or subscription exists.
            await using (var db = ScopedContext(options, tenant, user))
            {
                var module = await db.TenantModules.SingleAsync(x => x.TenantId == tenant);
                module.IsEnabled = false;
                await db.SaveChangesAsync();
            }
            using (var unavailable = await PublicLmsRequestAsync(
                client, HttpMethod.Post, publicRoute, body: new { token = second }))
            {
                Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
                AssertPublicLmsNoStore(unavailable);
            }
            await using (var db = ScopedContext(options, tenant, user))
            {
                var module = await db.TenantModules.SingleAsync(x => x.TenantId == tenant);
                module.IsEnabled = true;
                await db.SaveChangesAsync();
            }
            using (var validAgain = await PublicLmsRequestAsync(
                client, HttpMethod.Post, publicRoute, body: new { token = second }))
                Assert.Equal(HttpStatusCode.OK, validAgain.StatusCode);

            using (var disabled = await PublicLmsRequestAsync(
                client, HttpMethod.Post, route + "/disable", tenant, user, "Student",
                new { rowVersion = version }, csrf, cookie))
            {
                Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
                Assert.False((await PublicLmsDataAsync(disabled))
                    .GetProperty("enabled").GetBoolean());
                AssertPublicLmsNoStore(disabled);
            }
            using var invalidated = await PublicLmsRequestAsync(
                client, HttpMethod.Post, publicRoute, body: new { token = second });
            Assert.Equal(HttpStatusCode.NotFound, invalidated.StatusCode);
        }

        // The actual anonymous policy is enforced over HTTP, not just declared.
        var throttled = false;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            using var response = await PublicLmsRequestAsync(
                client, HttpMethod.Post, publicRoute,
                body: new { token = new string('a', 43) });
            AssertPublicLmsNoStore(response);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = true;
                break;
            }
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.True(throttled, "Anonymous public certificate verification must be rate limited.");
    }

    private static async Task<string> PublicLmsDataAsyncBootstrap(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
