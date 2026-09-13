using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
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

public class StudentDirectoryServiceTests
{
    [Fact]
    public async Task List_masks_contact_while_authorized_details_return_full_profile()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var student = await SeedAsync(context, 101);
        var service = new StudentDirectoryService(new GenericRepository<Student>(context), new TestCurrentUser(101), NullLogger<StudentDirectoryService>.Instance);

        var page = await service.GetPageAsync(new StudentDirectoryQueryDto { Search = student.StudentCode });
        var details = await service.GetAsync(student.PublicId);

        page.Success.Should().BeTrue();
        page.Data!.Items.Should().ContainSingle();
        page.Data.Items[0].MaskedMobile.Should().EndWith("5678").And.NotBe("+8801712345678");
        details.Success.Should().BeTrue();
        details.Data!.Phone.Should().Be("+8801712345678");
        details.Data.Guardians.Should().ContainSingle();
        details.Data.Enrollments.Should().ContainSingle();
    }

    [Fact]
    public async Task Foreign_tenant_student_is_not_visible()
    {
        var options = CreateOptions();
        Guid foreignReference;
        await using (var foreign = CreateContext(options, 202)) foreignReference = (await SeedAsync(foreign, 202)).PublicId;
        await using var current = CreateContext(options, 101);
        var service = new StudentDirectoryService(new GenericRepository<Student>(current), new TestCurrentUser(101), NullLogger<StudentDirectoryService>.Instance);

        var page = await service.GetPageAsync(new StudentDirectoryQueryDto());
        var details = await service.GetAsync(foreignReference);

        page.Data!.Items.Should().BeEmpty();
        details.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unauthorized_role_is_denied_before_data_access()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var service = new StudentDirectoryService(new GenericRepository<Student>(context), new TestCurrentUser(101, "Teacher"), NullLogger<StudentDirectoryService>.Instance);
        (await service.GetPageAsync(new StudentDirectoryQueryDto())).StatusCode.Should().Be(403);
    }

    private static async Task<Student> SeedAsync(EduOSDbContext context, long tenantId)
    {
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var unit = new Class { TenantId = tenantId, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, unit);
        await context.SaveChangesAsync();
        var section = new Section { TenantId = tenantId, ClassId = checked((int)unit.Id), Class = unit, Name = "A", Capacity = 50, IsActive = true };
        context.Add(section);
        await context.SaveChangesAsync();
        var student = new Student { TenantId = tenantId, PublicId = Guid.NewGuid(), StudentCode = $"STU-{Guid.NewGuid():N}", Roll = "12",
            FullName = "Rahim Uddin", FullNameBangla = "রহিম উদ্দিন", FatherName = "Karim Uddin", MotherName = "",
            DOB = new DateTime(2012, 2, 3), Gender = "Male", Phone = "+8801712345678", ClassId = checked((int)unit.Id), Class = unit,
            SectionId = checked((int)section.Id), Section = section, AcademicYearId = checked((int)year.Id), AcademicYear = year,
            AdmissionDate = new DateTime(2026, 9, 8), PreferredLanguage = "bn-BD", Status = "Active", IsActive = true };
        context.Add(student);
        await context.SaveChangesAsync();
        context.AddRange(
            new Guardian { TenantId = tenantId, PublicId = Guid.NewGuid(), StudentId = student.Id, Student = student, Name = "Karim Uddin", Relation = "Father", Phone = "+8801700000000", IsPrimary = true },
            new Enrollment { TenantId = tenantId, StudentId = checked((int)student.Id), Student = student, AcademicYearId = checked((int)year.Id), AcademicYear = year,
                ClassId = checked((int)unit.Id), Class = unit, SectionId = checked((int)section.Id), Section = section, Roll = "12", EnrollmentDate = new DateTime(2026, 9, 8), IsActive = true });
        await context.SaveChangesAsync();
        return student;
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"student-directory-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestCurrentUser(long tenantId, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Directory User";
        public string? Email => "directory@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
