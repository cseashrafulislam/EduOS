using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AdmissionEnrollmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approved_application_creates_student_guardian_enrollment_and_person_link_atomically()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var seed = await SeedAsync(context, 101, AdmissionApplicationStatus.Approved);
        var service = CreateService(context, new TestCurrentUser(101));

        var result = await service.AdmitAsync(seed.Application.PublicId, Request(seed));

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.StudentCode.Should().Be($"STU-{seed.Application.PublicId:N}".ToUpperInvariant());
        (await context.Students.CountAsync()).Should().Be(1);
        (await context.Guardians.CountAsync()).Should().Be(1);
        (await context.Enrollments.CountAsync()).Should().Be(1);
        (await context.Persons.CountAsync()).Should().Be(1);
        (await context.StudentPersonLinks.CountAsync()).Should().Be(1);
        (await context.AdmissionApplicants.SingleAsync()).Status.Should().Be(AdmissionApplicationStatus.Admitted);
        var privateAudits = await context.AuditLogs.Where(x => x.TableName == nameof(Student)
                                                               || x.TableName == nameof(Guardian)
                                                               || x.TableName == nameof(Enrollment)).ToListAsync();
        privateAudits.Should().NotBeEmpty();
        privateAudits.Should().OnlyContain(x => x.NewValue == null && x.OldValue == null);
    }

    [Fact]
    public async Task Repeated_admit_is_idempotent()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var seed = await SeedAsync(context, 101, AdmissionApplicationStatus.Approved);
        var service = CreateService(context, new TestCurrentUser(101));

        var first = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        var replay = await service.AdmitAsync(seed.Application.PublicId, Request(seed));

        first.Success.Should().BeTrue();
        replay.Success.Should().BeTrue();
        replay.Data!.StudentId.Should().Be(first.Data!.StudentId);
        (await context.Students.CountAsync()).Should().Be(1);
        (await context.Enrollments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Non_approved_application_and_foreign_section_are_rejected()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var pending = await SeedAsync(context, 101, AdmissionApplicationStatus.UnderReview);
        var service = CreateService(context, new TestCurrentUser(101));

        var wrongStatus = await service.AdmitAsync(pending.Application.PublicId, Request(pending));
        pending.Application.Status = AdmissionApplicationStatus.Approved;
        await context.SaveChangesAsync();
        var foreignSection = Request(pending);
        foreignSection.SectionId += 9999;
        var wrongSection = await service.AdmitAsync(pending.Application.PublicId, foreignSection);

        wrongStatus.StatusCode.Should().Be(409);
        wrongSection.StatusCode.Should().Be(409);
        (await context.Students.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Non_admission_role_is_denied()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var seed = await SeedAsync(context, 101, AdmissionApplicationStatus.Approved);
        var service = CreateService(context, new TestCurrentUser(101, "Teacher"));

        var result = await service.AdmitAsync(seed.Application.PublicId, Request(seed));

        result.StatusCode.Should().Be(403);
        (await context.Students.CountAsync()).Should().Be(0);
    }

    private static AdmissionEnrollmentService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<AdmissionApplicant>(context), new GenericRepository<Student>(context),
        new GenericRepository<Guardian>(context), new GenericRepository<Enrollment>(context), new GenericRepository<Person>(context),
        new GenericRepository<StudentPersonLink>(context), new GenericRepository<Section>(context), new GenericRepository<Group>(context),
        new TestUnitOfWork(context), user, new FixedTimeProvider(Now), NullLogger<AdmissionEnrollmentService>.Instance);

    private static AdmitAdmissionApplicationDto Request(SeedData seed) => new()
    {
        SectionId = seed.Section.Id, GroupId = seed.Group.Id, Roll = "12", RowVersion = Convert.ToBase64String(seed.Application.RowVersion)
    };

    private static async Task<SeedData> SeedAsync(EduOSDbContext context, long tenantId, AdmissionApplicationStatus status)
    {
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN", IsActive = true };
        var unit = new Class { TenantId = tenantId, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();
        var section = new Section { TenantId = tenantId, ClassId = checked((int)unit.Id), Class = unit, Name = "A", Capacity = 60, IsActive = true };
        var group = new Group { TenantId = tenantId, Name = "Science", Code = "SCI", IsActive = true };
        var application = new AdmissionApplicant { TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = $"APP-2026-{Guid.NewGuid():N}"[..17].ToUpperInvariant(), AcademicYearId = year.Id, AcademicYear = year,
            CampusId = campus.Id, Campus = campus, AcademicUnitId = unit.Id, AcademicUnit = unit, ApplicantName = "Rahim Uddin",
            ApplicantNameBangla = "রহিম উদ্দিন", DateOfBirth = new DateTime(2012, 2, 3), Gender = Gender.Male,
            PrimaryMobile = "+8801712345678", GuardianName = "Karim Uddin", GuardianRelation = "Father",
            GuardianMobile = "+8801700000000", PreferredLanguage = "bn-BD", Status = status, SubmittedAtUtc = Now.UtcDateTime };
        context.AddRange(section, group, application);
        await context.SaveChangesAsync();
        return new SeedData(application, section, group);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"admission-enrollment-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed record SeedData(AdmissionApplicant Application, Section Section, Group Group);
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class TestUnitOfWork(EduOSDbContext context) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task CommitTransactionAsync() => Task.CompletedTask;
        public Task RollbackTransactionAsync() => Task.CompletedTask;
        public IExecutionStrategy CreateExecutionStrategy() => context.Database.CreateExecutionStrategy();
        public void Dispose() { }
    }

    private sealed class TestCurrentUser(long tenantId, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Admission User";
        public string? Email => "admission@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
