using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.App.Middleware;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
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
/// Real LMS service and SQL-backed HTTP controller authorization. Synthetic tenants
/// only; the intercepted private storage never touches institutional files.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record SqlLmsSubmission(long SubmissionId, Guid FileReference,
        long CampusId, long AcademicEnrollmentId, string StorageKey, byte[] Bytes);

    private static TestServer LmsSubmissionSqlHttpServer(DbContextOptions<EduOSDbContext> options,
        Mock<IFileUploadService> storage, bool moduleEnabled = true)
    {
        var modules = new Mock<ITenantModuleService>();
        modules.Setup(x => x.IsCurrentTenantModuleAvailableAsync(It.IsAny<string>()))
            .ReturnsAsync(moduleEnabled);
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
                services.AddRateLimiter(x => x.AddFixedWindowLimiter("ApiPolicy", y =>
                {
                    y.PermitLimit = 1000;
                    y.Window = TimeSpan.FromMinutes(1);
                    y.QueueLimit = 0;
                }));
                services.AddScoped(sp => new EduOSDbContext(
                    options, sp.GetRequiredService<IHttpContextAccessor>()));
                services.AddScoped<ICurrentUserService, PrivateHttpUser>();
                services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EduOSDbContext>());
                services.AddScoped<ILmsWorkflowService, LmsWorkflowService>();
                services.AddSingleton(storage.Object);
                services.AddSingleton(TimeProvider.System);
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
                        long.TryParse(http.User.FindFirstValue("TenantId"), out var tenant))
                        http.Items["TenantId"] = tenant;
                    await next();
                });
                app.UseAuthorization();
                app.UseEndpoints(x => x.MapControllers());
            }));
    }

    private static async Task<SqlLmsSubmission> SeedLmsSubmissionAsync(Fixture fixture)
    {
        var bytes = "SQL verified private learner submission"u8.ToArray();
        var key = $"tenant-{fixture.TenantId}/lms/submission-{Guid.NewGuid():N}.pdf";
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var academic = await db.StudentEnrollments.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
        var student = await db.Students.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
        student.UserId = fixture.UserId;
        var course = new Course
        {
            TenantId = fixture.TenantId,
            Code = "SQL-LMS-PRIVATE-" + Guid.NewGuid().ToString("N")[..10],
            Title = "Protected SQL submission", IsActive = true
        };
        var file = new FileAsset
        {
            TenantId = fixture.TenantId, StorageProvider = "LocalPrivate",
            StorageKey = key, OriginalFileName = "submission.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            UploadedByUserId = fixture.UserId, MalwareScanStateCode = "Clean",
            Visibility = FileVisibility.Private, IsVerifiedSafe = true
        };
        db.AddRange(course, file);
        await db.SaveChangesAsync();
        var assignment = new Assignment
        {
            TenantId = fixture.TenantId, CourseId = course.Id,
            Title = "Private assignment", IsPublished = true
        };
        var enrollment = new CourseEnrollment
        {
            TenantId = fixture.TenantId, ClientRequestId = Guid.NewGuid(),
            CourseId = course.Id, StudentId = student.Id,
            StudentEnrollmentId = academic.Id, State = CourseEnrollmentState.Active
        };
        db.AddRange(assignment, enrollment);
        await db.SaveChangesAsync();
        var submission = new AssignmentSubmission
        {
            TenantId = fixture.TenantId, AssignmentId = assignment.Id,
            CourseEnrollmentId = enrollment.Id, FileAssetId = file.Id,
            State = LearningSubmissionState.Submitted, SubmittedAt = DateTime.UtcNow
        };
        db.Add(submission);
        await db.SaveChangesAsync();
        return new SqlLmsSubmission(submission.Id, file.PublicId, academic.CampusId,
            academic.Id, key, bytes);
    }

    private static async Task<HttpResponseMessage> LmsSubmissionGetAsync(HttpClient client,
        SqlLmsSubmission file, string? role, long tenant, long user, Guid? guessedFile = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/lms/assignment-work/submissions/{file.SubmissionId}/files/" +
            $"{guessedFile ?? file.FileReference}/download");
        if (role != null) request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task SQL_HTTP_LMS_submission_private_bytes_follow_tenant_student_campus_and_academic_revocation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var first = await SeedAsync(connection);
        var second = await SeedAsync(connection);
        var own = await SeedLmsSubmissionAsync(first);
        var foreign = await SeedLmsSubmissionAsync(second);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(own.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = own.Bytes });
        using var server = LmsSubmissionSqlHttpServer(first.Options, storage);
        using var client = server.CreateClient();

        using (var allowed = await LmsSubmissionGetAsync(client, own, "Student",
            first.TenantId, first.UserId))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(own.Bytes, await allowed.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", allowed.Content.Headers.ContentType?.MediaType);
            Assert.Contains("attachment", allowed.Content.Headers.ContentDisposition?.ToString() ?? "");
            AssertPrivateCache(allowed);
            Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("sandbox", allowed.Headers.GetValues("Content-Security-Policy").Single());
        }

        foreach (var (file, role, tenant, user, guessed, expected) in new[]
        {
            (foreign, "Student", first.TenantId, first.UserId, (Guid?)null, HttpStatusCode.NotFound),
            (own, "Student", second.TenantId, second.UserId, (Guid?)null, HttpStatusCode.NotFound),
            (own, "Student", first.TenantId, first.UserId, (Guid?)Guid.NewGuid(), HttpStatusCode.NotFound),
            (own, "Guardian", first.TenantId, first.UserId, (Guid?)null, HttpStatusCode.Forbidden)
        })
        {
            using var denied = await LmsSubmissionGetAsync(client, file, role, tenant, user, guessed);
            Assert.Equal(expected, denied.StatusCode);
            AssertPrivateCache(denied);
            Assert.NotEqual(own.Bytes, await denied.Content.ReadAsByteArrayAsync());
        }
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);

        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            (await db.Campuses.SingleAsync(x => x.Id == own.CampusId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        using (var denied = await LmsSubmissionGetAsync(client, own, "Student",
            first.TenantId, first.UserId))
        {
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            AssertPrivateCache(denied);
        }
        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            (await db.Campuses.SingleAsync(x => x.Id == own.CampusId)).IsActive = true;
            var academic = await db.StudentEnrollments.SingleAsync(x => x.Id == own.AcademicEnrollmentId);
            academic.IsCurrent = false;
            academic.State = EnrollmentState.Withdrawn;
            await db.SaveChangesAsync();
        }
        using (var denied = await LmsSubmissionGetAsync(client, own, "Student",
            first.TenantId, first.UserId))
        {
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            AssertPrivateCache(denied);
        }
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);

        using var moduleDisabled = LmsSubmissionSqlHttpServer(first.Options, storage, moduleEnabled: false);
        using var deniedClient = moduleDisabled.CreateClient();
        using var disabled = await LmsSubmissionGetAsync(deniedClient, own, "Student",
            first.TenantId, first.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
        AssertPrivateCache(disabled);
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Once);
    }
}
