using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Portals;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class SelfServicePortalServiceTests
{
    [Fact]
    public async Task Transport_returns_only_assignment_for_directly_linked_student_in_current_tenant()
    {
        await using var context = CreateContext(out var accessor);
        var own = Student(10, 99, "OWN");
        var other = Student(10, 100, "OTHER");
        var foreign = Student(20, 99, "FOREIGN");
        context.Students.AddRange(own, other, foreign);
        var ownRoute = new Route { TenantId = 10, Name = "Own route", Fare = 500 };
        var otherRoute = new Route { TenantId = 10, Name = "Other route", Fare = 600 };
        var foreignRoute = new Route { TenantId = 20, Name = "Foreign route", Fare = 700 };
        context.Routes.AddRange(ownRoute, otherRoute, foreignRoute);
        await context.SaveChangesAsync();
        var ownVehicle = new Vehicle { TenantId = 10, VehicleNo = "OWN-BUS", Capacity = 30, RouteId = ownRoute.Id, DriverName = "Own Driver" };
        var otherVehicle = new Vehicle { TenantId = 10, VehicleNo = "OTHER-BUS", Capacity = 30, RouteId = otherRoute.Id };
        var foreignVehicle = new Vehicle { TenantId = 20, VehicleNo = "FOREIGN-BUS", Capacity = 30, RouteId = foreignRoute.Id };
        context.Vehicles.AddRange(ownVehicle, otherVehicle, foreignVehicle);
        await context.SaveChangesAsync();
        context.StudentTransports.AddRange(
            Assignment(10, own.Id, ownVehicle.Id, ownRoute.Id, "Gate A"),
            Assignment(10, other.Id, otherVehicle.Id, otherRoute.Id, "Gate B"),
            Assignment(20, foreign.Id, foreignVehicle.Id, foreignRoute.Id, "Gate C"));
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetTransportAsync(own.PublicId);

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].RouteName.Should().Be("Own route");
        result.Data[0].VehicleNo.Should().Be("OWN-BUS");
        result.Data[0].PickupPoint.Should().Be("Gate A");
    }

    [Fact]
    public async Task Transport_allows_guardian_link_but_rejects_unlinked_student()
    {
        await using var context = CreateContext(out var accessor);
        var linked = Student(10, null, "CHILD");
        var unlinked = Student(10, null, "OTHER");
        context.Students.AddRange(linked, unlinked);
        await context.SaveChangesAsync();
        context.Guardians.Add(new Guardian { TenantId = 10, StudentId = linked.Id, UserId = 99, Name = "Guardian", Relation = "Father", Phone = "01700000000" });
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);
        var service = CreateService(context, new TestCurrentUser(10, 99, "Guardian"));

        var linkedResult = await service.GetTransportAsync(linked.PublicId);
        var unlinkedResult = await service.GetTransportAsync(unlinked.PublicId);

        linkedResult.Success.Should().BeTrue();
        unlinkedResult.Success.Should().BeFalse();
        unlinkedResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Transport_rejects_same_user_student_reference_from_another_tenant()
    {
        await using var context = CreateContext(out var accessor);
        var own = Student(10, 99, "OWN");
        var foreign = Student(20, 99, "FOREIGN");
        context.Students.AddRange(own, foreign);
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetTransportAsync(foreign.PublicId);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Homework_returns_only_students_class_and_section()
    {
        await using var context = CreateContext(out var accessor);
        SetTenant(accessor, 10);
        var own = Student(10, 99, "OWN");
        context.Students.Add(own);
        await context.SaveChangesAsync();
        context.Homeworks.AddRange(Homework(10, 1, 1, "Visible"), Homework(10, 1, 2, "Other section"));
        await context.SaveChangesAsync();

        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetHomeworkAsync(own.PublicId);

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].Title.Should().Be("Visible");
    }

    [Fact]
    public async Task Homework_rejects_unlinked_student_for_guardian()
    {
        await using var context = CreateContext(out var accessor);
        var linked = Student(10, null, "CHILD");
        var unlinked = Student(10, null, "OTHER");
        context.Students.AddRange(linked, unlinked);
        await context.SaveChangesAsync();
        context.Guardians.Add(new Guardian { TenantId = 10, StudentId = linked.Id, UserId = 99, Name = "Guardian", Relation = "Mother", Phone = "01700000000" });
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);

        var result = await CreateService(context, new TestCurrentUser(10, 99, "Guardian")).GetHomeworkAsync(unlinked.PublicId);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    private static EduOSDbContext CreateContext(out HttpContextAccessor accessor)
    {
        accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"self-service-{Guid.NewGuid():N}").Options, accessor);
    }

    private static void SetTenant(HttpContextAccessor accessor, long tenantId) => accessor.HttpContext!.Items["TenantId"] = tenantId;

    private static SelfServicePortalService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Student>(context), new GenericRepository<Guardian>(context), new GenericRepository<StudentAttendance>(context),
        new GenericRepository<ExamResult>(context), new GenericRepository<StudentInvoice>(context), new GenericRepository<Payment>(context),
        new GenericRepository<StudentTransport>(context), new GenericRepository<Homework>(context), currentUser, NullLogger<SelfServicePortalService>.Instance);

    private static Student Student(long tenantId, long? userId, string code) => new()
    {
        TenantId = tenantId, UserId = userId, StudentCode = code, Roll = code, FullName = code, FatherName = "Father", MotherName = "Mother",
        DOB = DateTime.UtcNow.Date.AddYears(-10), Gender = "Male", ClassId = 1, SectionId = 1, AcademicYearId = 1,
        AdmissionDate = DateTime.UtcNow.Date, IsActive = true
    };

    private static StudentTransport Assignment(long tenantId, long studentId, long vehicleId, long routeId, string pickup) => new()
    {
        TenantId = tenantId, ClientRequestId = Guid.NewGuid(), StudentId = studentId, VehicleId = vehicleId, RouteId = routeId,
        PickupPoint = pickup, StartDate = DateTime.UtcNow.Date, MonthlyFare = 500, IsActive = true
    };

    private static Homework Homework(long tenantId, long classId, long sectionId, string title) => new()
    {
        TenantId = tenantId, ClassId = classId, SectionId = sectionId, SubjectId = 1, TeacherId = 1, Title = title,
        AssignedDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(2)
    };

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => role;
        public string? Email => "portal@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => string.Equals(value, role, StringComparison.OrdinalIgnoreCase);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
