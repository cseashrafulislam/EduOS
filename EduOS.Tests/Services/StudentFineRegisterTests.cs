using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentFineRegisterTests
{
    [Fact]
    public async Task Only_institution_admin_can_read_fine_register()
    {
        await using var db = Database();
        var result = await new FinanceAdjustmentService(db, new TestUser(false), TimeProvider.System)
            .GetFineRegisterAsync(new StudentFineRegisterQueryDto());
        result.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 101)]
    [InlineData(10001, 25)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        await using var db = Database();
        var result = await new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System)
            .GetFineRegisterAsync(new StudentFineRegisterQueryDto { Page = page, PageSize = size });
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Register_filters_campus_and_status_without_treating_fines_as_income()
    {
        await using var db = Database();
        var student = new Student { TenantId = 41, StudentCode = "S-41", FullName = "Test Student" };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var a = new StudentEnrollment { TenantId = 41, StudentId = student.Id, CampusId = 7, RollNo = "1" };
        var b = new StudentEnrollment { TenantId = 41, StudentId = student.Id, CampusId = 8, RollNo = "2" };
        db.StudentEnrollments.AddRange(a, b);
        var rule = new FineRule { TenantId = 41, Name = "Late", Amount = 20m };
        db.FineRules.Add(rule);
        await db.SaveChangesAsync();
        db.StudentFines.AddRange(
            new StudentFine { TenantId = 41, StudentEnrollmentId = a.Id, FineRuleId = rule.Id,
                FineDate = new DateOnly(2026, 10, 10), Amount = 20m },
            new StudentFine { TenantId = 41, StudentEnrollmentId = a.Id, FineRuleId = rule.Id,
                FineDate = new DateOnly(2026, 10, 11), Amount = 10m, IsWaived = true },
            new StudentFine { TenantId = 41, StudentEnrollmentId = b.Id, FineRuleId = rule.Id,
                FineDate = new DateOnly(2026, 10, 12), Amount = 20m });
        await db.SaveChangesAsync();

        var service = new FinanceAdjustmentService(db, new TestUser(true), TimeProvider.System);
        var result = await service.GetFineRegisterAsync(new StudentFineRegisterQueryDto {
            CampusId = 7, Waived = false, Page = 1, PageSize = 10
        });
        result.Success.Should().BeTrue();
        result.Data!.Fines.TotalCount.Should().Be(1);
        result.Data.Fines.Items.Single().EnrollmentReference.Should().Be(a.PublicId);
        result.Data.RecordedTotal.Should().Be(30m);
        result.Data.WaivedTotal.Should().Be(10m);
        result.Data.OutstandingAdministrativeTotal.Should().Be(20m);
        result.Data.IsPostedToLedger.Should().BeFalse();
        db.Journals.Should().BeEmpty();
        db.StudentInvoices.Should().BeEmpty();
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fine-register-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
