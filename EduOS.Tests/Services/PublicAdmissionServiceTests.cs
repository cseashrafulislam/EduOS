using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class PublicAdmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_exposes_only_published_assessment_result_for_verified_applicant()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"public-admission-{Guid.NewGuid():N}")
            .Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        await using var context = new EduOSDbContext(options, http);
        var tenant = new Tenant
        {
            Name = "Example School",
            Code = "example-school",
            Email = "school@example.test",
            OwnerName = "Owner",
            IsActive = true,
            IsOnboardingComplete = true
        };
        var module = new ProductModule
        {
            Code = "ADMISSION",
            Name = "Admission",
            Category = "Academic",
            IsActive = true
        };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();

        context.TenantModules.Add(new TenantModule
        {
            TenantId = tenant.Id,
            ProductModuleId = module.Id,
            IsEnabled = true
        });
        var year = new AcademicYear
        {
            TenantId = tenant.Id,
            Name = "2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsActive = true
        };
        var campus = new Campus
        {
            TenantId = tenant.Id,
            Name = "Main Campus",
            Code = "MAIN",
            IsActive = true
        };
        var unit = new Class
        {
            TenantId = tenant.Id,
            Name = "Class Six",
            NumericValue = 6,
            IsActive = true
        };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();

        var applicant = new AdmissionApplicant
        {
            TenantId = tenant.Id,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = "APP-2026-0001",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            ApplicantName = "Applicant",
            DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male,
            PrimaryMobile = "+8801712345678",
            PreferredLanguage = "bn-BD",
            Status = AdmissionApplicationStatus.UnderReview,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();

        var publishedTest = new AdmissionTest
        {
            TenantId = tenant.Id,
            Name = "Admission Test",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            TestDate = new DateTime(2026, 9, 20),
            TotalMarks = 100,
            PassMarks = 40,
            DurationMinutes = 60,
            IsPublished = true,
            PublishedAtUtc = Now.UtcDateTime
        };
        var unpublishedTest = new AdmissionTest
        {
            TenantId = tenant.Id,
            Name = "Private Draft Test",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            TestDate = new DateTime(2026, 9, 21),
            TotalMarks = 100,
            PassMarks = 40,
            DurationMinutes = 60,
            IsPublished = false
        };
        context.AddRange(publishedTest, unpublishedTest);
        await context.SaveChangesAsync();

        context.AddRange(
            new AdmissionResult
            {
                TenantId = tenant.Id,
                AdmissionTestId = publishedTest.Id,
                ApplicantId = applicant.Id,
                ObtainedMarks = 88,
                Percentage = 88,
                IsPassed = true,
                MeritPosition = 2,
                ResultStatus = "Passed"
            },
            new AdmissionResult
            {
                TenantId = tenant.Id,
                AdmissionTestId = unpublishedTest.Id,
                ApplicantId = applicant.Id,
                ObtainedMarks = 99,
                Percentage = 99,
                IsPassed = true,
                MeritPosition = 1,
                ResultStatus = "Passed"
            });
        await context.SaveChangesAsync();

        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<TenantModule>(context),
            new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context),
            new GenericRepository<AdmissionResult>(context),
            new GenericRepository<AcademicYear>(context),
            new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<Class>(context),
            context,
            http,
            new FixedTimeProvider(Now),
            NullLogger<PublicAdmissionService>.Instance);

        var result = await service.GetStatusAsync("example-school", applicant.PublicId, "01712345678");

        result.Success.Should().BeTrue();
        result.Data!.Assessment.Should().NotBeNull();
        result.Data.Assessment!.TestName.Should().Be("Admission Test");
        result.Data.Assessment.ObtainedMarks.Should().Be(88);
        result.Data.Assessment.MeritPosition.Should().Be(2);
    }

    [Fact]
    public async Task Status_requires_matching_mobile_even_with_valid_reference()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"public-admission-mobile-{Guid.NewGuid():N}")
            .Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        await using var context = new EduOSDbContext(options, http);

        var tenant = new Tenant
        {
            Name = "Example School",
            Code = "example-school",
            Email = "school@example.test",
            OwnerName = "Owner",
            IsActive = true,
            IsOnboardingComplete = true
        };
        var module = new ProductModule { Code = "ADMISSION", Name = "Admission", Category = "Academic", IsActive = true };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();
        context.Add(new TenantModule { TenantId = tenant.Id, ProductModuleId = module.Id, IsEnabled = true });
        var applicant = new AdmissionApplicant
        {
            TenantId = tenant.Id,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = "APP-2026-0001",
            ApplicantName = "Applicant",
            DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male,
            PrimaryMobile = "+8801712345678",
            PreferredLanguage = "bn-BD",
            Status = AdmissionApplicationStatus.Submitted,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();

        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<TenantModule>(context),
            new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context),
            new GenericRepository<AdmissionResult>(context),
            new GenericRepository<AcademicYear>(context),
            new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<Class>(context),
            context,
            http,
            new FixedTimeProvider(Now),
            NullLogger<PublicAdmissionService>.Instance);

        var result = await service.GetStatusAsync("example-school", applicant.PublicId, "01700000000");

        result.StatusCode.Should().Be(404);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
