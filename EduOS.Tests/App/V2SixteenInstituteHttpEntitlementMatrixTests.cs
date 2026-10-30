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
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Executable, synthetic 16-institute V2 entitlement and HTTP response matrix.
/// These are representative tenant configurations, NOT real customer presets or
/// a substitute for SQL-backed module-selection, data-isolation or institute UAT.
/// Uses the production module authorization handler, routes and private headers.
/// </summary>
public sealed class V2SixteenInstituteHttpEntitlementMatrixTests
{
    private sealed record Institute(int Id, string Category, bool Lms, bool Library, bool Hostel, bool Transport)
    {
        public bool Has(string module) => module.ToUpperInvariant() switch
        {
            "LMS" => Lms, "LIBRARY" => Library, "HOSTEL" => Hostel,
            "TRANSPORT" => Transport, _ => false
        };
    }

    // Release contract: 2 primary-college, 3 madrasa, 2 training, 1 coaching,
    // 3 online, 3 university and 2 private teacher institutes.
    private static readonly Institute[] Cohort =
    [
        new(101, "PrimaryCollege", false, true, true, true),
        new(102, "PrimaryCollege", true, true, false, true),
        new(103, "Madrasa", false, true, true, false),
        new(104, "Madrasa", true, false, false, true),
        new(105, "Madrasa", false, false, false, false),
        new(106, "Training", true, true, false, false),
        new(107, "Training", true, false, true, false),
        new(108, "Coaching", true, false, false, false),
        new(109, "Online", true, false, false, false),
        new(110, "Online", true, true, false, false),
        new(111, "Online", true, false, false, true),
        new(112, "University", true, true, true, true),
        new(113, "University", true, true, false, true),
        new(114, "University", false, true, true, true),
        new(115, "PrivateTeacher", true, false, false, false),
        new(116, "PrivateTeacher", false, false, false, false)
    ];

    private sealed class MatrixAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Matrix-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "901"),
                new Claim(ClaimTypes.Role, role)
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed record Endpoint(string Module, string Role, string Path, string Marker);

    private static readonly Endpoint[] Endpoints =
    [
        new("LMS", "Student", "/api/lms/assignment-work/submissions/77/files", "data"),
        new("LIBRARY", "Student", "/api/library/digital/00000000-0000-0000-0000-000000000077/download", "MATRIX-LIBRARY"),
        new("HOSTEL", "Guardian", "/api/hostel/guardian-stays/export", "MATRIX-HOSTEL"),
        new("TRANSPORT", "TransportManager",
            "/api/transport/boarding-manifest/export?operatingDate=2026-10-30", "MATRIX-TRANSPORT")
    ];

    private static TestServer Server()
    {
        var accessor = new HttpContextAccessor();
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .Returns((string code) =>
            {
                var tenant = accessor.HttpContext?.Request.Headers["X-Matrix-Tenant"].ToString();
                return Task.FromResult(int.TryParse(tenant, out var id) &&
                    Cohort.FirstOrDefault(x => x.Id == id)?.Has(code) == true);
            });

        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.GetSubmissionFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>.SuccessResponse(
                Array.Empty<AssignmentSubmissionFileDto>()));

        var library = new Mock<ILibraryDigitalResourceService>();
        library.Setup(x => x.DownloadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<DocumentFileDownloadDto>.SuccessResponse(
                new DocumentFileDownloadDto
                {
                    Content = "MATRIX-LIBRARY"u8.ToArray(),
                    ContentType = "application/octet-stream",
                    FileName = "matrix.bin"
                }));

        var hostel = new Mock<IGuardianHostelStayService>();
        hostel.Setup(x => x.ExportCurrentPageCsvAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<string>.SuccessResponse("MATRIX-HOSTEL"));

        var transport = new Mock<ITransportService>();
        transport.Setup(x => x.ExportBoardingManifestCsvAsync(
                It.IsAny<DateOnly>(), It.IsAny<long?>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<string>.SuccessResponse("MATRIX-TRANSPORT"));

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddSingleton<IHttpContextAccessor>(accessor);
                services.AddAuthentication("Matrix")
                    .AddScheme<AuthenticationSchemeOptions, MatrixAuthenticationHandler>(
                        "Matrix", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(modules.Object);
                services.AddSingleton(lms.Object);
                services.AddSingleton(library.Object);
                services.AddSingleton(hostel.Object);
                services.AddSingleton(transport.Object);
                services.AddAntiforgery();
                services.AddRateLimiter(options =>
                    options.AddFixedWindowLimiter("ApiPolicy", limiter =>
                    {
                        limiter.PermitLimit = 1000;
                        limiter.Window = TimeSpan.FromMinutes(1);
                        limiter.QueueLimit = 0;
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
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string path, int? tenant, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (tenant.HasValue)
            request.Headers.TryAddWithoutValidation("X-Matrix-Tenant", tenant.Value.ToString());
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Matrix-Role", role);
        return await client.SendAsync(request);
    }

    private static async Task AssertResultAsync(
        HttpResponseMessage response, HttpStatusCode expected, string marker)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("nosniff",
                Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            var body = await response.Content.ReadAsStringAsync();
            if (expected == HttpStatusCode.OK)
                Assert.Contains(marker, body, StringComparison.OrdinalIgnoreCase);
            else
                Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Sixteen_institute_matrix_enforces_all_four_V2_module_gates_across_session_switches()
    {
        Assert.Equal(16, Cohort.Length);
        foreach (var (category, count) in new[]
        {
            ("PrimaryCollege", 2), ("Madrasa", 3), ("Training", 2),
            ("Coaching", 1), ("Online", 3), ("University", 3),
            ("PrivateTeacher", 2)
        })
            Assert.Equal(count, Cohort.Count(x => x.Category == category));

        using var server = Server();
        using var client = server.CreateClient();
        // One HTTP client deliberately switches institutions repeatedly, including
        // returning to a previous institute after a different entitlement set.
        foreach (var institute in Cohort.Concat(Cohort.Reverse()))
            foreach (var endpoint in Endpoints)
            {
                var expected = institute.Has(endpoint.Module)
                    ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                await AssertResultAsync(await GetAsync(client, endpoint.Path,
                    institute.Id, endpoint.Role), expected, endpoint.Marker);
            }
    }

    [Fact]
    public async Task Unknown_tenant_anonymous_and_wrong_role_never_release_V2_private_bytes_or_exports()
    {
        using var server = Server();
        using var client = server.CreateClient();
        foreach (var endpoint in Endpoints)
        {
            await AssertResultAsync(await GetAsync(client, endpoint.Path, null, endpoint.Role),
                HttpStatusCode.Forbidden, endpoint.Marker);
            await AssertResultAsync(await GetAsync(client, endpoint.Path, 999999, endpoint.Role),
                HttpStatusCode.Forbidden, endpoint.Marker);
            await AssertResultAsync(await GetAsync(client, endpoint.Path, 112, null),
                HttpStatusCode.Unauthorized, endpoint.Marker);
            await AssertResultAsync(await GetAsync(client, endpoint.Path, 112, "UnrelatedRole"),
                HttpStatusCode.Forbidden, endpoint.Marker);
        }
    }
}
