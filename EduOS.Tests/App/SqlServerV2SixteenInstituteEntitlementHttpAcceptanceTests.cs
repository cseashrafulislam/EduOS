using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.App.Middleware;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Seed;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Disposable SQL Server -> real TenantModuleService -> production module policy
/// -> HTTP controller acceptance. The four domain response bodies are inert
/// markers: the test proves entitlement/selection/revocation before a domain
/// service is called, not the domain owners' separately tested data queries.
/// The cohort is synthetic; it is not a customer configuration or UAT signoff.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerV2SixteenInstituteEntitlementHttpAcceptanceTests
{
    private sealed record Institute(
        string Category, string TypeCode, string Plan, SubscriptionState Subscription,
        bool Expired, string? BlockFeature,
        bool Lms, bool Library, bool Hostel, bool Transport)
    {
        public bool Selected(string module) => module switch
        {
            "LMS" => Lms, "LIBRARY" => Library,
            "HOSTEL" => Hostel, "TRANSPORT" => Transport, _ => false
        };

        public bool Available(string module) =>
            Selected(module) &&
            Plan is "PRO" or "ENTERPRISE" &&
            Subscription is SubscriptionState.Active or SubscriptionState.Trial or SubscriptionState.Grace &&
            !Expired && BlockFeature != FeatureFor(module);
    }

    private static readonly Institute[] Cohort =
    [
        new("PrimaryCollege", "SCHOOL_COLLEGE", "PRO", SubscriptionState.Active, false, null, true, true, true, true),
        new("PrimaryCollege", "SCHOOL_COLLEGE", "BASIC", SubscriptionState.Active, false, null, true, true, false, true),
        new("Madrasa", "MADRASA", "PRO", SubscriptionState.Active, false, null, false, true, true, false),
        new("Madrasa", "MADRASA", "ENTERPRISE", SubscriptionState.Active, false, null, true, false, false, true),
        new("Madrasa", "MADRASA", "PRO", SubscriptionState.PendingPayment, false, null, true, true, true, true),
        new("Training", "TRAINING_INSTITUTE", "PRO", SubscriptionState.Active, false, null, true, true, false, false),
        new("Training", "TRAINING_INSTITUTE", "PRO", SubscriptionState.Active, true, null, true, false, true, false),
        new("Coaching", "COACHING_CENTER", "PRO", SubscriptionState.Active, false, null, true, false, false, false),
        new("Online", "LMS_PROVIDER", "PRO", SubscriptionState.Active, false, null, true, false, false, false),
        new("Online", "LMS_PROVIDER", "PRO", SubscriptionState.Active, false, null, true, true, false, false),
        new("Online", "LMS_PROVIDER", "ENTERPRISE", SubscriptionState.Active, false, null, true, false, false, true),
        new("University", "UNIVERSITY", "PRO", SubscriptionState.Active, false, null, true, true, true, true),
        new("University", "UNIVERSITY", "PRO", SubscriptionState.Active, false, "LIBRARY", true, true, false, true),
        new("University", "UNIVERSITY", "PRO", SubscriptionState.Active, false, null, false, true, true, true),
        new("PrivateTeacher", "PRIVATE_TUTOR", "PRO", SubscriptionState.Active, false, null, true, false, false, false),
        new("PrivateTeacher", "PRIVATE_TUTOR", "TRIAL", SubscriptionState.Active, false, null, false, false, false, false)
    ];

    private static string FeatureFor(string module) => module == "LMS" ? "ONLINE_EXAM" : module;

    private sealed record SeededInstitute(Institute Configuration, long TenantId);

    private sealed record Endpoint(string Module, string Role, string Path, string Marker);

    private static readonly Endpoint[] Endpoints =
    [
        new("LMS", "Student", "/api/lms/assignment-work/submissions/77/files", ""),
        new("LIBRARY", "Student",
            "/api/library/digital/00000000-0000-0000-0000-000000000077/download", "SQL-MATRIX-LIBRARY"),
        new("HOSTEL", "Guardian", "/api/hostel/guardian-stays/export", "SQL-MATRIX-HOSTEL"),
        new("TRANSPORT", "TransportManager",
            "/api/transport/boarding-manifest/export?operatingDate=2026-10-30", "SQL-MATRIX-TRANSPORT")
    ];

    private sealed class SqlMatrixAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Matrix-Role"].ToString();
            var tenant = Request.Headers["X-Matrix-Tenant"].ToString();
            if (string.IsNullOrWhiteSpace(role) || !long.TryParse(tenant, out var id) || id <= 0)
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "901"),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenant)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class SqlMatrixUser(IHttpContextAccessor accessor) : ICurrentUserService
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public long TenantId => long.TryParse(Principal?.FindFirstValue("TenantId"), out var id) ? id : 0;
        public long UserId => 901;
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

    private static TenantModuleService RealModules(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Tenant>(db),
        new GenericRepository<ProductModule>(db),
        new GenericRepository<InstitutionTypeModule>(db),
        new GenericRepository<TenantModule>(db),
        new GenericRepository<ProductModuleFeature>(db),
        new GenericRepository<PlanFeature>(db),
        new GenericRepository<TenantFeature>(db),
        new GenericRepository<TenantSubscription>(db),
        db, user, NullLogger<TenantModuleService>.Instance);

    private static TestServer Server(DbContextOptions<EduOSDbContext> options)
    {
        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.GetSubmissionFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>.SuccessResponse(
                Array.Empty<AssignmentSubmissionFileDto>()));
        var library = new Mock<ILibraryDigitalResourceService>();
        library.Setup(x => x.DownloadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
            {
                Content = "SQL-MATRIX-LIBRARY"u8.ToArray(),
                ContentType = "application/octet-stream",
                FileName = "matrix.bin"
            }));
        var hostel = new Mock<IGuardianHostelStayService>();
        hostel.Setup(x => x.ExportCurrentPageCsvAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<string>.SuccessResponse("SQL-MATRIX-HOSTEL"));
        var transport = new Mock<ITransportService>();
        transport.Setup(x => x.ExportBoardingManifestCsvAsync(
                It.IsAny<DateOnly>(), It.IsAny<long?>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<string>.SuccessResponse("SQL-MATRIX-TRANSPORT"));

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("SqlMatrix")
                    .AddScheme<AuthenticationSchemeOptions, SqlMatrixAuth>("SqlMatrix", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                // Real module service is scoped to the request; never inject it into
                // a singleton authorization handler and accidentally reuse tenant state.
                services.AddScoped<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, SqlMatrixUser>();
                services.AddScoped<ITenantModuleService>(sp => RealModules(
                    sp.GetRequiredService<EduOSDbContext>(),
                    sp.GetRequiredService<ICurrentUserService>()));
                services.AddSingleton(lms.Object);
                services.AddSingleton(library.Object);
                services.AddSingleton(hostel.Object);
                services.AddSingleton(transport.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(LmsSubmissionFilesController).Assembly);
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
                        long.TryParse(http.User.FindFirstValue("TenantId"), out var id))
                        http.Items["TenantId"] = id;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(x => x.MapControllers());
            }));
    }

    private static async Task<List<SeededInstitute>> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var setup = new EduOSDbContext(options);
        await SubscriptionSeeder.SeedAsync(setup);
        await PlatformCatalogSeeder.SeedAsync(setup);
        var types = await setup.InstitutionTypeDefinitions.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Id);
        var plans = await setup.SubscriptionPlans.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Id);
        var modules = await setup.ProductModules.AsNoTracking()
            .Where(x => x.Code == "LMS" || x.Code == "LIBRARY" ||
                x.Code == "HOSTEL" || x.Code == "TRANSPORT")
            .ToDictionaryAsync(x => x.Code, x => x.Id);
        var features = await setup.Features.AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Id);
        Assert.Equal(4, modules.Count);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var tenants = Cohort.Select((c, index) => new Tenant
        {
            Code = $"CI-V2-{suffix}-{index:D2}",
            Name = $"SQL V2 {c.Category} {index:D2}",
            Email = $"v2-{suffix}-{index:D2}@example.test",
            InstitutionTypeDefinitionId = types[c.TypeCode],
            State = TenantState.Active,
            EmailVerifiedAt = DateTime.UtcNow
        }).ToArray();
        setup.Tenants.AddRange(tenants);
        await setup.SaveChangesAsync();

        var seeded = new List<SeededInstitute>(Cohort.Length);
        for (var i = 0; i < Cohort.Length; i++)
        {
            var c = Cohort[i];
            var tenantId = tenants[i].Id;
            seeded.Add(new SeededInstitute(c, tenantId));
            await using var db = new EduOSDbContext(options);
            using (db.BeginSystemTenantScope(tenantId))
            {
                db.TenantSubscriptions.Add(new TenantSubscription
                {
                    TenantId = tenantId, SubscriptionPlanId = plans[c.Plan],
                    State = c.Subscription, BillingCycleCode = "Monthly",
                    CurrencyCode = "BDT", StartsAt = DateTime.UtcNow.AddDays(-30),
                    EndsAt = DateTime.UtcNow.AddDays(c.Expired ? -1 : 30)
                });
                foreach (var module in modules)
                    db.TenantModules.Add(new TenantModule
                    {
                        TenantId = tenantId, ProductModuleId = module.Value,
                        IsEnabled = c.Selected(module.Key),
                        EnabledAt = c.Selected(module.Key) ? DateTime.UtcNow : null
                    });
                if (c.BlockFeature != null)
                    db.TenantFeatures.Add(new TenantFeature
                    {
                        TenantId = tenantId, FeatureId = features[c.BlockFeature],
                        IsEnabled = false, EffectiveFrom = DateTime.UtcNow.AddDays(-1),
                        Reason = "SQL matrix tenant-specific restriction"
                    });
                await db.SaveChangesAsync();
            }
        }
        return seeded;
    }

    private static async Task AssertAccessAsync(
        HttpClient client, long tenant, Endpoint endpoint, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Path);
        request.Headers.TryAddWithoutValidation("X-Matrix-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Matrix-Role", endpoint.Role);
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        if (endpoint.Marker.Length > 0)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (allowed) Assert.Contains(endpoint.Marker, body);
            else Assert.DoesNotContain(endpoint.Marker, body);
        }
    }

    [Fact]
    public async Task Real_sql_plan_preset_selection_and_restriction_gates_all_four_V2_http_surfaces_across_16_tenants()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var institutes = await SeedAsync(options);
        Assert.Equal(16, institutes.Count);
        foreach (var (category, count) in new[]
        {
            ("PrimaryCollege", 2), ("Madrasa", 3), ("Training", 2),
            ("Coaching", 1), ("Online", 3), ("University", 3),
            ("PrivateTeacher", 2)
        })
            Assert.Equal(count, institutes.Count(x => x.Configuration.Category == category));

        using var server = Server(options);
        using var client = server.CreateClient();
        foreach (var institute in institutes.Concat(institutes.AsEnumerable().Reverse()))
            foreach (var endpoint in Endpoints)
                await AssertAccessAsync(client, institute.TenantId, endpoint,
                    institute.Configuration.Available(endpoint.Module));

        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, long.MaxValue, endpoint, false);
    }

    [Fact]
    public async Task Subscription_expiry_module_deactivation_and_feature_revocation_take_effect_on_next_http_request()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var institutes = await SeedAsync(options);
        var tenant = institutes.Single(x => x.Configuration.Category == "University" &&
            x.Configuration.BlockFeature == null && x.Configuration.Lms);
        using var server = Server(options);
        using var client = server.CreateClient();
        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, tenant.TenantId, endpoint, true);

        await using (var db = new EduOSDbContext(options))
        using (db.BeginSystemTenantScope(tenant.TenantId))
        {
            var subscription = await db.TenantSubscriptions.SingleAsync();
            subscription.EndsAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, tenant.TenantId, endpoint, false);

        await using (var db = new EduOSDbContext(options))
        using (db.BeginSystemTenantScope(tenant.TenantId))
        {
            var subscription = await db.TenantSubscriptions.SingleAsync();
            subscription.EndsAt = DateTime.UtcNow.AddDays(30);
            var libraryId = await db.ProductModules.Where(x => x.Code == "LIBRARY")
                .Select(x => x.Id).SingleAsync();
            var selection = await db.TenantModules.SingleAsync(x => x.ProductModuleId == libraryId);
            selection.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, tenant.TenantId, endpoint,
                endpoint.Module != "LIBRARY");

        await using (var db = new EduOSDbContext(options))
        using (db.BeginSystemTenantScope(tenant.TenantId))
        {
            var libraryId = await db.ProductModules.Where(x => x.Code == "LIBRARY")
                .Select(x => x.Id).SingleAsync();
            var selection = await db.TenantModules.SingleAsync(x => x.ProductModuleId == libraryId);
            selection.IsEnabled = true;
            var featureId = await db.Features.Where(x => x.Code == "LIBRARY")
                .Select(x => x.Id).SingleAsync();
            db.TenantFeatures.Add(new TenantFeature
            {
                TenantId = tenant.TenantId, FeatureId = featureId, IsEnabled = false,
                EffectiveFrom = DateTime.UtcNow.AddMinutes(-1),
                Reason = "Revoked during active HTTP session"
            });
            await db.SaveChangesAsync();
        }
        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, tenant.TenantId, endpoint,
                endpoint.Module != "LIBRARY");

        await using (var db = new EduOSDbContext(options))
        using (db.BeginSystemTenantScope(tenant.TenantId))
        {
            var feature = await db.TenantFeatures.SingleAsync();
            feature.IsEnabled = true;
            await db.SaveChangesAsync();
        }
        foreach (var endpoint in Endpoints)
            await AssertAccessAsync(client, tenant.TenantId, endpoint, true);
    }
}
