using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
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
/// Actual in-process HTTP routing, authentication, module-policy and response tests.
/// The services are controlled at the HTTP seam; the separate SQL Server V2 boundary
/// suite exercises the real service queries and revocation transactions. This suite
/// deliberately does not pretend that mocked responses prove SQL tenant isolation.
/// </summary>
public sealed class V2PrivateHttpBoundaryIntegrationTests
{
    private sealed class HeaderTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1001"),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private static TestServer CreateServer(
        bool moduleEnabled,
        Mock<ILibraryDigitalResourceService>? library = null,
        Mock<ILmsWorkflowService>? lms = null,
        Mock<ILmsCertificateGuardianService>? guardians = null)
    {
        var module = new Mock<ITenantModuleService>();
        module.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(moduleEnabled);

        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddHttpContextAccessor();
                services.AddAuthentication("V2Test")
                    .AddScheme<AuthenticationSchemeOptions, HeaderTestAuthenticationHandler>(
                        "V2Test", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, ModuleAuthorizationPolicyProvider>();
                services.AddSingleton<IAuthorizationHandler, ModuleAccessHandler>();
                services.AddSingleton(module.Object);
                services.AddSingleton(library?.Object ?? Mock.Of<ILibraryDigitalResourceService>());
                services.AddSingleton(lms?.Object ?? Mock.Of<ILmsWorkflowService>());
                services.AddSingleton(guardians?.Object ?? Mock.Of<ILmsCertificateGuardianService>());
                services.AddAntiforgery();
                services.AddRateLimiter(options =>
                    options.AddFixedWindowLimiter("ApiPolicy", limiter =>
                    {
                        limiter.PermitLimit = 1000;
                        limiter.Window = TimeSpan.FromMinutes(1);
                        limiter.QueueLimit = 0;
                    }));
                services.AddControllers()
                    .AddApplicationPart(typeof(LibraryDigitalResourcesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string path, string? role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role != null)
            request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        return await client.SendAsync(request);
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.Contains("no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Library_download_uses_real_http_file_response_with_private_cache_and_content_headers()
    {
        var reference = Guid.NewGuid();
        var bytes = "%PDF-1.4\nV2 private HTTP fixture"u8.ToArray();
        var library = new Mock<ILibraryDigitalResourceService>();
        library.Setup(x => x.DownloadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid requested, CancellationToken _) => Task.FromResult(
                requested == reference
                    ? ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
                    {
                        Content = bytes, ContentType = "application/pdf",
                        FileName = "private-evidence.pdf"
                    })
                    : ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Not found.", 404)));

        using var server = CreateServer(true, library);
        using var client = server.CreateClient();

        using var allowed = await GetAsync(client,
            $"/api/library/digital/{reference}/download", "Student");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", allowed.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", allowed.Content.Headers.ContentDisposition?.ToString() ?? "");
        AssertPrivateNoStore(allowed);
        Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", allowed.Headers.GetValues("Referrer-Policy").Single());

        using var guessed = await GetAsync(client,
            $"/api/library/digital/{Guid.NewGuid()}/download", "Student");
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
        Assert.NotEqual(bytes, await guessed.Content.ReadAsByteArrayAsync());
        AssertPrivateNoStore(guessed);
        library.Verify(x => x.DownloadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(null, true, HttpStatusCode.Unauthorized)]
    [InlineData("Guardian", true, HttpStatusCode.Forbidden)]
    [InlineData("Student", false, HttpStatusCode.Forbidden)]
    public async Task Library_private_bytes_never_reach_service_when_http_auth_or_module_denies(
        string? role, bool moduleEnabled, HttpStatusCode expected)
    {
        var library = new Mock<ILibraryDigitalResourceService>();
        using var server = CreateServer(moduleEnabled, library);
        using var client = server.CreateClient();

        using var response = await GetAsync(client,
            $"/api/library/digital/{Guid.NewGuid()}/download", role);
        Assert.Equal(expected, response.StatusCode);
        library.Verify(x => x.DownloadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LMS_submission_file_http_route_preserves_ownership_denials_and_never_caches_metadata()
    {
        const long own = 1001, otherStudent = 1002, foreignTenant = 9001;
        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.GetSubmissionFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long id, CancellationToken _) => Task.FromResult(
                id == own
                    ? ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>.SuccessResponse(
                        Array.Empty<AssignmentSubmissionFileDto>())
                    : ApiResponse<IReadOnlyList<AssignmentSubmissionFileDto>>.ErrorResponse(
                        "Submission unavailable.", id == otherStudent ? 403 : 404)));

        using var server = CreateServer(true, lms: lms);
        using var client = server.CreateClient();
        foreach (var (id, expected) in new[]
        {
            (own, HttpStatusCode.OK),
            (otherStudent, HttpStatusCode.Forbidden),
            (foreignTenant, HttpStatusCode.NotFound),
            (long.MaxValue, HttpStatusCode.NotFound)
        })
        {
            using var response = await GetAsync(client,
                $"/api/lms/assignment-work/submissions/{id}/files", "Student");
            Assert.Equal(expected, response.StatusCode);
            AssertPrivateNoStore(response);
            if (expected != HttpStatusCode.OK)
                Assert.DoesNotContain("fileAssetReference",
                    await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        lms.Verify(x => x.GetSubmissionFilesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Theory]
    [InlineData("Guardian", HttpStatusCode.NotFound)]
    [InlineData("Parent", HttpStatusCode.NotFound)]
    [InlineData("Student", HttpStatusCode.Forbidden)]
    public async Task Revoked_guardian_certificate_is_unavailable_over_real_http(
        string role, HttpStatusCode expected)
    {
        var guardians = new Mock<ILmsCertificateGuardianService>();
        guardians.Setup(x => x.GetSharedCertificateAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<LmsCertificateDto>.ErrorResponse(
                "Sharing revoked.", 404));
        using var server = CreateServer(true, guardians: guardians);
        using var client = server.CreateClient();

        using var response = await GetAsync(client,
            $"/api/lms/certificate-sharing/{Guid.NewGuid()}", role);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
            guardians.Verify(x => x.GetSharedCertificateAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        else
            guardians.Verify(x => x.GetSharedCertificateAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        if (expected == HttpStatusCode.NotFound) AssertPrivateNoStore(response);
    }

}
