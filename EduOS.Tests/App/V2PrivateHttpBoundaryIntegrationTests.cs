using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduOS.App.Authorization;
using EduOS.App.Middleware;
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
        Mock<ILmsCertificateGuardianService>? guardians = null,
        Mock<ILmsCourseContentService>? courseContent = null)
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
                services.AddSingleton(courseContent?.Object ?? Mock.Of<ILmsCourseContentService>());
                services.AddSingleton(guardians?.Object ?? Mock.Of<ILmsCertificateGuardianService>());
                services.AddAntiforgery();
                services.AddRateLimiter(options =>
                    options.AddFixedWindowLimiter("ApiPolicy", limiter =>
                    {
                        limiter.PermitLimit = 1000;
                        limiter.Window = TimeSpan.FromMinutes(1);
                        limiter.QueueLimit = 0;
                    }));
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(LibraryDigitalResourcesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseV2PrivateApiResponseHeaders();
                app.UseRateLimiter();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapControllerRoute("v2-private-views", "{controller=Home}/{action=Index}/{id?}");
                });
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
        AssertPrivateNoStore(response);
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

    [Fact]
    public async Task LMS_student_picker_is_manager_only_and_never_caches_student_details()
    {
        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.SearchEligibleStudentsAsync("ST-", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<LmsStudentChoiceDto>>.SuccessResponse(
                [new LmsStudentChoiceDto { StudentReference = Guid.NewGuid(), StudentCode = "ST-22", StudentName = "Learner" }]));
        using var server = CreateServer(true, lms: lms);
        using var client = server.CreateClient();
        using var manager = await GetAsync(client, "/api/lms/students/search?search=ST-&take=25", "TenantAdmin");
        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        Assert.Contains("ST-22", await manager.Content.ReadAsStringAsync());
        AssertPrivateNoStore(manager);
        using var teacher = await GetAsync(client, "/api/lms/students/search?search=ST-&take=25", "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, teacher.StatusCode);
        AssertPrivateNoStore(teacher);
        using var student = await GetAsync(client, "/api/lms/students/search?search=ST-&take=25", "Student");
        Assert.Equal(HttpStatusCode.Forbidden, student.StatusCode);
        using var anonymous = await GetAsync(client, "/api/lms/students/search?search=ST-&take=25", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        lms.Verify(x => x.SearchEligibleStudentsAsync("ST-", 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LMS_rubric_reads_and_score_breakdown_have_private_http_and_role_guards()
    {
        var reference = Guid.NewGuid();
        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.GetAssignmentRubricAsync(reference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AssignmentRubricDto>.SuccessResponse(new AssignmentRubricDto
            {
                AssignmentReference = reference,
                Criteria = [new AssignmentRubricCriterionDto { Title = "Accuracy", MaxMarks = 10 }]
            }));
        lms.Setup(x => x.GetSubmissionRubricScoresAsync(33, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<IReadOnlyList<AssignmentRubricScoreDto>>.SuccessResponse(
                [new AssignmentRubricScoreDto { AwardedMarks = 8 }]));
        using var server = CreateServer(true, lms: lms);
        using var client = server.CreateClient();
        using var rubric = await GetAsync(client, $"/api/lms/assignments/{reference}/rubric", "Student");
        Assert.Equal(HttpStatusCode.OK, rubric.StatusCode);
        Assert.Contains("Accuracy", await rubric.Content.ReadAsStringAsync());
        AssertPrivateNoStore(rubric);
        using var scores = await GetAsync(client, "/api/lms/assignments/submissions/33/rubric-scores", "Student");
        Assert.Equal(HttpStatusCode.OK, scores.StatusCode);
        AssertPrivateNoStore(scores);
        using var denied = await GetAsync(client, $"/api/lms/assignments/{reference}/rubric", "Guardian");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = await GetAsync(client, "/api/lms/assignments/submissions/33/rubric-scores", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        lms.Verify(x => x.GetAssignmentRubricAsync(reference, It.IsAny<CancellationToken>()), Times.Once);
        lms.Verify(x => x.GetSubmissionRubricScoresAsync(33, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LMS_submission_download_returns_only_authorized_private_bytes_with_attachment_headers()
    {
        const long ownSubmission = 1201;
        var reference = Guid.NewGuid();
        var bytes = "%PDF-1.4\nStudent assignment evidence"u8.ToArray();
        var lms = new Mock<ILmsWorkflowService>();
        lms.Setup(x => x.DownloadSubmissionFileAsync(
                It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((long id, Guid requested, CancellationToken _) => Task.FromResult(
                id == ownSubmission && requested == reference
                    ? ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
                    {
                        Content = bytes, ContentType = "application/pdf", FileName = "course-work.pdf"
                    })
                    : ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Submission file not found.", 404)));

        using var server = CreateServer(true, lms: lms);
        using var client = server.CreateClient();
        var url = $"/api/lms/assignment-work/submissions/{ownSubmission}/files/{reference}/download";
        using var allowed = await GetAsync(client, url, "Student");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", allowed.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", allowed.Content.Headers.ContentDisposition?.ToString() ?? "");
        AssertPrivateNoStore(allowed);
        Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", allowed.Headers.GetValues("Content-Security-Policy").Single());

        using var teacher = await GetAsync(client, url, "Teacher");
        Assert.Equal(HttpStatusCode.OK, teacher.StatusCode);
        Assert.Equal(bytes, await teacher.Content.ReadAsByteArrayAsync());
        AssertPrivateNoStore(teacher);

        using var guessed = await GetAsync(client,
            $"/api/lms/assignment-work/submissions/{ownSubmission}/files/{Guid.NewGuid()}/download", "Student");
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
        AssertPrivateNoStore(guessed);
        Assert.NotEqual(bytes, await guessed.Content.ReadAsByteArrayAsync());
        lms.Verify(x => x.DownloadSubmissionFileAsync(It.IsAny<long>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Theory]
    [InlineData(null, true, HttpStatusCode.Unauthorized)]
    [InlineData("Guardian", true, HttpStatusCode.Forbidden)]
    [InlineData("Student", false, HttpStatusCode.Forbidden)]
    public async Task LMS_submission_bytes_are_blocked_before_service_for_denied_role_or_module(
        string? role, bool moduleEnabled, HttpStatusCode expected)
    {
        var lms = new Mock<ILmsWorkflowService>();
        using var server = CreateServer(moduleEnabled, lms: lms);
        using var client = server.CreateClient();
        using var response = await GetAsync(client,
            $"/api/lms/assignment-work/submissions/1201/files/{Guid.NewGuid()}/download", role);
        Assert.Equal(expected, response.StatusCode);
        AssertPrivateNoStore(response);
        lms.Verify(x => x.DownloadSubmissionFileAsync(It.IsAny<long>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LMS_assignment_handout_private_http_download_delivers_only_verified_service_bytes()
    {
        var bytes = "%PDF-1.4\nPrivate course handout"u8.ToArray();
        var content = new Mock<ILmsCourseContentService>();
        content.Setup(x => x.DownloadAssignmentAttachmentAsync(It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns((long id, CancellationToken _) => Task.FromResult(id == 81
                ? ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
                {
                    Content = bytes, ContentType = "application/pdf", FileName = "lesson-guide.pdf"
                })
                : ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Attachment not found.", 404)));
        using var server = CreateServer(true, courseContent: content);
        using var client = server.CreateClient();
        using var allowed = await GetAsync(client, "/api/lms/assignment-attachments/81/download", "Student");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(bytes, await allowed.Content.ReadAsByteArrayAsync());
        Assert.Contains("attachment", allowed.Content.Headers.ContentDisposition?.ToString() ?? "");
        AssertPrivateNoStore(allowed);
        Assert.Equal("sandbox", allowed.Headers.GetValues("Content-Security-Policy").Single());
        using var denied = await GetAsync(client, "/api/lms/assignment-attachments/82/download", "Student");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        AssertPrivateNoStore(denied);
        using var guardian = await GetAsync(client, "/api/lms/assignment-attachments/81/download", "Guardian");
        Assert.Equal(HttpStatusCode.Forbidden, guardian.StatusCode);
        content.Verify(x => x.DownloadAssignmentAttachmentAsync(It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
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

    [Theory]
    [InlineData("/LmsGuardianCertificates/Print?reference=00000000-0000-0000-0000-000000000001", null, true, HttpStatusCode.Unauthorized)]
    [InlineData("/LmsGuardianCertificates/Print?reference=00000000-0000-0000-0000-000000000001", "Student", true, HttpStatusCode.Forbidden)]
    [InlineData("/LmsGuardianCertificates/Print?reference=00000000-0000-0000-0000-000000000001", "Guardian", false, HttpStatusCode.Forbidden)]
    [InlineData("/hostel/guardian-stays/print", null, true, HttpStatusCode.Unauthorized)]
    [InlineData("/hostel/guardian-stays/print", "Student", true, HttpStatusCode.Forbidden)]
    [InlineData("/hostel/guardian-stays/print", "Parent", false, HttpStatusCode.Forbidden)]
    public async Task V2_private_print_denials_cannot_be_cached_even_before_MVC_filters(
        string path, string? role, bool moduleEnabled, HttpStatusCode expected)
    {
        using var server = CreateServer(moduleEnabled);
        using var client = server.CreateClient();
        using var response = await GetAsync(client, path, role);
        Assert.Equal(expected, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

}
