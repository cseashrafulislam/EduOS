using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;
namespace EduOS.Tests.Services;

public class StudentPromotionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Promotion_atomically_closes_source_creates_target_and_updates_student()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, "TenantAdmin"));

        var result = await service.PromoteAsync(seeded.StudentReference, Request(seeded));

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Decision.Should().Be(StudentProgressionDecision.Promoted);
        result.Data.FromEnrollmentId.Should().Be(seeded.EnrollmentId);
        result.Data.ToEnrollmentId.Should().BeGreaterThan(0);

        var student = await context.Students.SingleAsync();
        student.AcademicYearId.Should().Be(2);
        student.ClassId.Should().Be(2);
        student.SectionId.Should().Be(2);
        student.Roll.Should().Be("5");

        var enrollments = await context.Enrollments.OrderBy(x => x.Id).ToListAsync();
        enrollments.Should().HaveCount(2);
        enrollments.Single(x => x.Id == seeded.EnrollmentId).IsActive.Should().BeFalse();
        enrollments.Single(x => x.Id != seeded.EnrollmentId).IsActive.Should().BeTrue();

        var record = await context.StudentPromotionRecords.SingleAsync();
        record.FromEnrollmentId.Should().Be(seeded.EnrollmentId);
        record.ToEnrollmentId.Should().Be(result.Data.ToEnrollmentId);
        record.ProcessedByUserId.Should().Be(7);

        var history = await service.GetHistoryAsync(seeded.StudentReference);
        history.Success.Should().BeTrue();
        history.Data.Should().ContainSingle(x => x.Reference == record.PublicId);
    }

    [Fact]
    public async Task Same_client_request_is_idempotent()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, "TenantAdmin"));
        var request = Request(seeded);

        var first = await service.PromoteAsync(seeded.StudentReference, request);
        var second = await service.PromoteAsync(seeded.StudentReference, request);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data!.AlreadyProcessed.Should().BeTrue();
        second.Data.Reference.Should().Be(first.Data!.Reference);
        (await context.Enrollments.CountAsync()).Should().Be(2);
        (await context.StudentPromotionRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Lower_or_same_class_is_rejected_for_promotion()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, "TenantAdmin"));
        var request = Request(seeded);
        request.TargetClassId = 1;
        request.TargetSectionId = 1;

        var result = await service.PromoteAsync(seeded.StudentReference, request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        (await context.StudentPromotionRecords.CountAsync()).Should().Be(0);
        (await context.Enrollments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Repeated_student_must_remain_in_same_class()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "Principal");
        var service = CreateService(context, new TestCurrentUser(101, "Principal"));
        var request = Request(seeded);
        request.Decision = StudentProgressionDecision.Repeated;

        var invalid = await service.PromoteAsync(seeded.StudentReference, request);

        invalid.StatusCode.Should().Be(409);

        request.TargetClassId = 1;
        request.TargetSectionId = 1;
        request.ClientRequestId = Guid.NewGuid();
        var valid = await service.PromoteAsync(seeded.StudentReference, request);

        valid.Success.Should().BeTrue();
        valid.Data!.Decision.Should().Be(StudentProgressionDecision.Repeated);
        valid.Data.ClassId.Should().Be(1);
    }

    [Fact]
    public async Task Cross_tenant_student_reference_is_neutral_not_found()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 202, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(202, "TenantAdmin"));

        var result = await service.PromoteAsync(seeded.StudentReference, Request(seeded));

        result.StatusCode.Should().Be(404);
        (await context.StudentPromotionRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Stale_student_version_and_wrong_section_fail_before_writes()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, "TenantAdmin"));
        var stale = Request(seeded);
        stale.StudentRowVersion = Convert.ToBase64String([1]);

        (await service.PromoteAsync(seeded.StudentReference, stale)).StatusCode.Should().Be(409);

        var wrongSection = Request(seeded);
        wrongSection.TargetSectionId = 1;
        (await service.PromoteAsync(seeded.StudentReference, wrongSection)).StatusCode.Should().Be(409);

        (await context.StudentPromotionRecords.CountAsync()).Should().Be(0);
        (await context.Enrollments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_role_is_denied_and_promotion_history_is_append_only()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using (var teacherContext = CreateContext(options, 101, "Teacher"))
        {
            var teacherService = CreateService(teacherContext, new TestCurrentUser(101, "Teacher"));
            (await teacherService.PromoteAsync(seeded.StudentReference, Request(seeded))).StatusCode
                .Should().Be(403);
        }

        await using var adminContext = CreateContext(options, 101, "TenantAdmin");
        var adminService = CreateService(adminContext, new TestCurrentUser(101, "TenantAdmin"));
        (await adminService.PromoteAsync(seeded.StudentReference, Request(seeded))).Success.Should().BeTrue();
        var record = await adminContext.StudentPromotionRecords.SingleAsync();
        record.Note = "Changed";

        var action = () => adminContext.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    private static StudentPromotionService CreateService(
        EduOSDbContext context,
        ICurrentUserService currentUser) => new(
        new GenericRepository<Student>(context),
        new GenericRepository<Enrollment>(context),
        new GenericRepository<StudentPromotionRecord>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<Class>(context),
        new GenericRepository<Section>(context),
        new GenericRepository<Group>(context),
        context,
        currentUser,
        new FixedTimeProvider(Now),
        NullLogger<StudentPromotionService>.Instance);

    private static PromoteStudentRequestDto Request(SeededStudent seeded) => new()
    {
        ClientRequestId = Guid.NewGuid(),
        SourceEnrollmentId = seeded.EnrollmentId,
        TargetAcademicYearId = 2,
        TargetClassId = 2,
        TargetSectionId = 2,
        TargetRoll = "5",
        Decision = StudentProgressionDecision.Promoted,
        StudentRowVersion = seeded.StudentRowVersion,
        SourceEnrollmentRowVersion = seeded.EnrollmentRowVersion,
        Note = "Annual progression"
    };

    private static async Task<SeededStudent> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var context = CreateContext(options, 101, "TenantAdmin");
        context.AcademicYears.AddRange(
            new AcademicYear
            {
                Id = 1,
                TenantId = 101,
                Name = "2025",
                StartDate = new DateTime(2025, 1, 1),
                EndDate = new DateTime(2025, 12, 31),
                IsActive = true
            },
            new AcademicYear
            {
                Id = 2,
                TenantId = 101,
                Name = "2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsActive = true
            });
        context.Classes.AddRange(
            new Class { Id = 1, TenantId = 101, Name = "Class One", NumericValue = 1, IsActive = true },
            new Class { Id = 2, TenantId = 101, Name = "Class Two", NumericValue = 2, IsActive = true });
        context.Sections.AddRange(
            new Section { Id = 1, TenantId = 101, ClassId = 1, Name = "A", Capacity = 30, IsActive = true },
            new Section { Id = 2, TenantId = 101, ClassId = 2, Name = "A", Capacity = 30, IsActive = true });
        var student = new Student
        {
            Id = 10,
            TenantId = 101,
            StudentCode = "STU-0001",
            Roll = "4",
            FullName = "Promotion Student",
            FatherName = "Father",
            MotherName = "Mother",
            DOB = new DateTime(2015, 1, 1),
            Gender = "Male",
            ClassId = 1,
            SectionId = 1,
            AcademicYearId = 1,
            AdmissionDate = new DateTime(2025, 1, 1),
            Status = "Active",
            IsActive = true
        };
        var enrollment = new Enrollment
        {
            TenantId = 101,
            StudentId = 10,
            AcademicYearId = 1,
            ClassId = 1,
            SectionId = 1,
            Roll = "4",
            EnrollmentDate = new DateTime(2025, 1, 1),
            IsActive = true
        };
        context.Students.Add(student);
        context.Enrollments.Add(enrollment);
        await context.SaveChangesAsync();
        return new SeededStudent(
            student.PublicId,
            enrollment.Id,
            Convert.ToBase64String(student.RowVersion),
            Convert.ToBase64String(enrollment.RowVersion));
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"student-promotion-{Guid.NewGuid():N}")
            .Options;

    private static EduOSDbContext CreateContext(
        DbContextOptions<EduOSDbContext> options,
        long tenantId,
        string role)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed class TestCurrentUser(long tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Promotion Admin";
        public string? Email => "promotion@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => string.Equals(value, role, StringComparison.Ordinal);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS promotion tests";
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record SeededStudent(
        Guid StudentReference,
        long EnrollmentId,
        string StudentRowVersion,
        string EnrollmentRowVersion);
}
