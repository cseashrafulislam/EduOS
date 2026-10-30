using System.Net;
using System.Net.Http;
using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.App.Middleware;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Disposable SQL Server integration, including concurrent consent and IDOR probes.
/// Uses the existing seeded tenant fixture; no live institute data.</summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LmsProgressGuardianService ProgressSharing(
        EduOSDbContext db, string role, long tenant, long userId) =>
        new(db, new CertificateActor(role, tenant, userId), TimeProvider.System,
            NullLogger<LmsProgressGuardianService>.Instance);

    private static TestServer ProgressGuardianHttpServer(DbContextOptions<EduOSDbContext> options)
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
                    .AddScheme<AuthenticationSchemeOptions, PrivateHttpAuth>("SqlPrivateTest", _ => { });
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
                services.AddScoped<ILmsProgressGuardianService, LmsProgressGuardianService>();
                services.AddSingleton(TimeProvider.System);
                services.AddControllersWithViews()
                    .AddApplicationPart(typeof(LmsProgressSharingController).Assembly);
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
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    private static async Task<HttpResponseMessage> ProgressGuardianGetAsync(
        HttpClient client, string path, string? role, long tenant, long user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role != null) request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        request.Headers.TryAddWithoutValidation("X-Test-Tenant", tenant.ToString());
        request.Headers.TryAddWithoutValidation("X-Test-User", user.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task SQL_LMS_guardian_progress_requires_enrollment_consent_and_revocation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var other = await SeedAsync(connection);
        long enrollmentId, guardianLinkId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var learner = await db.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            learner.UserId = fixture.UserId;
            var person = new Person { FullName = "Disposable progress guardian" };
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            var guardian = new Guardian
            {
                TenantId = fixture.TenantId, PersonId = person.Id, UserId = fixture.UserId,
                FullName = "Progress guardian", IsActive = true
            };
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "SQL-LPG-" + Guid.NewGuid().ToString("N")[..12],
                Title = "Progress consent course", IsActive = true
            };
            db.AddRange(guardian, course);
            await db.SaveChangesAsync();
            var link = new StudentGuardian
            {
                TenantId = fixture.TenantId, StudentId = learner.Id,
                GuardianId = guardian.Id, RelationCode = "Parent"
            };
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = learner.Id,
                StudentEnrollmentId = academic.Id, ClientRequestId = Guid.NewGuid(),
                State = CourseEnrollmentState.Active, EnrolledAt = DateTime.UtcNow
            };
            db.AddRange(link, enrollment);
            await db.SaveChangesAsync();
            db.Lessons.Add(new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published progress", DisplayOrder = 1, IsPublished = true
            });
            await db.SaveChangesAsync();
            enrollmentId = enrollment.Id; guardianLinkId = link.Id;
        }

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var guardian = ProgressSharing(db, "Guardian", fixture.TenantId, fixture.UserId);
            Assert.Empty((await guardian.SharedAsync(1, 20)).Data!.Items);
            Assert.Equal(404, (await guardian.LessonsAsync(enrollmentId, 1, 20)).StatusCode);
            Assert.Equal(404, (await ProgressSharing(db, "Guardian", other.TenantId, fixture.UserId)
                .LessonsAsync(enrollmentId, 1, 20)).StatusCode);
            Assert.Equal(404, (await ProgressSharing(db, "Teacher", fixture.TenantId, fixture.UserId)
                .LessonsAsync(enrollmentId, 1, 20)).StatusCode);
        }

        var request = new GrantLmsProgressGuardianRequestDto
        {
            CourseEnrollmentId = enrollmentId, GuardianLinkId = guardianLinkId,
            ClientRequestId = Guid.NewGuid()
        };
        async Task<EduOS.Core.Common.ApiResponse<LmsProgressGuardianGrantDto>> Attempt()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await ProgressSharing(db, "Student", fixture.TenantId, fixture.UserId).GrantAsync(request);
        }
        var attempts = await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));

        using var server = ProgressGuardianHttpServer(fixture.Options);
        using var client = server.CreateClient();
        const string exportPath = "/api/lms/progress-sharing/mine/export?page=1&pageSize=20";
        using (var denied = await ProgressGuardianGetAsync(client, exportPath,
            "Student", fixture.TenantId, fixture.UserId))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var wrongTenant = await ProgressGuardianGetAsync(client, exportPath,
            "Guardian", other.TenantId, fixture.UserId))
        {
            Assert.Equal(HttpStatusCode.OK, wrongTenant.StatusCode);
            Assert.DoesNotContain("Progress consent course",
                await wrongTenant.Content.ReadAsStringAsync());
        }
        using (var export = await ProgressGuardianGetAsync(client, exportPath,
            "Guardian", fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Contains("attachment", export.Content.Headers.ContentDisposition?.ToString() ?? "");
            Assert.Contains("no-store", export.Headers.CacheControl?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Progress consent course", await export.Content.ReadAsStringAsync());
        }

        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(1, await db.LmsProgressGuardianGrants.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.CourseEnrollmentId == enrollmentId));
            var guardian = ProgressSharing(db, "Parent", fixture.TenantId, fixture.UserId);
            var shared = await guardian.SharedAsync(1, 20);
            Assert.True(shared.Success, shared.Message);
            var row = Assert.Single(shared.Data!.Items);
            Assert.Equal(enrollmentId, row.CourseEnrollmentId);
            Assert.Equal(1, row.PublishedLessons);
            Assert.Single((await guardian.LessonsAsync(enrollmentId, 1, 20)).Data!.Items);
            var student = ProgressSharing(db, "Student", fixture.TenantId, fixture.UserId);
            var grants = await student.GrantsAsync(enrollmentId);
            var grant = Assert.Single(grants.Data!);
            Assert.Equal(409, (await student.RevokeAsync(grant.Reference,
                new RevokeLmsProgressGuardianRequestDto
                { RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }) })).StatusCode);
            Assert.True((await student.RevokeAsync(grant.Reference,
                new RevokeLmsProgressGuardianRequestDto { RowVersion = grant.RowVersion })).Success);
            Assert.Empty((await guardian.SharedAsync(1, 20)).Data!.Items);
            Assert.Equal(404, (await guardian.LessonsAsync(enrollmentId, 1, 20)).StatusCode);
        }
        using (var export = await ProgressGuardianGetAsync(client, exportPath,
            "Parent", fixture.TenantId, fixture.UserId))
            Assert.DoesNotContain("Progress consent course", await export.Content.ReadAsStringAsync());

        // A new consent cannot override a retired guardian relationship.
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var renewed = await ProgressSharing(db, "Student", fixture.TenantId, fixture.UserId)
                .GrantAsync(new GrantLmsProgressGuardianRequestDto
                {
                    CourseEnrollmentId = enrollmentId, GuardianLinkId = guardianLinkId,
                    ClientRequestId = Guid.NewGuid()
                });
            Assert.True(renewed.Success, renewed.Message);
        }
        using (var export = await ProgressGuardianGetAsync(client, exportPath,
            "Guardian", fixture.TenantId, fixture.UserId))
            Assert.Contains("Progress consent course", await export.Content.ReadAsStringAsync());
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x => x.Id == guardianLinkId)).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        using (var export = await ProgressGuardianGetAsync(client, exportPath,
            "Guardian", fixture.TenantId, fixture.UserId))
            Assert.DoesNotContain("Progress consent course", await export.Content.ReadAsStringAsync());
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
            Assert.Equal(404, (await ProgressSharing(db, "Guardian", fixture.TenantId, fixture.UserId)
                .LessonsAsync(enrollmentId, 1, 20)).StatusCode);
    }
}
