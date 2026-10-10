using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.Services;

public class TransportServiceTests
{
    [Fact]
    public async Task Close_with_matching_row_version_closes_assignment()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, true, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today), RowVersion = Convert.ToBase64String(assignment.RowVersion)
        });
        response.Success.Should().BeTrue();
        response.Data!.State.Should().Be(TransportAssignmentState.Closed);
        var saved = await context.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.State.Should().Be(TransportAssignmentState.Closed);
        saved.EndDate.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public async Task Close_with_stale_row_version_is_rejected_without_mutation()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, true, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today),
            RowVersion = Convert.ToBase64String(new byte[] { 8,7,6,5,4,3,2,1 })
        });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        var saved = await context.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.State.Should().Be(TransportAssignmentState.Active);
        saved.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task Closing_an_already_closed_assignment_remains_idempotent()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, false, [1,2,3,4,5,6,7,8]);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(assignment.PublicId, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today),
            RowVersion = Convert.ToBase64String(new byte[] { 9,9,9,9,9,9,9,9 })
        });
        response.Success.Should().BeTrue();
        response.Data!.State.Should().Be(TransportAssignmentState.Closed);
    }

    [Fact]
    public async Task Close_cannot_mutate_assignment_from_another_tenant()
    {
        var options = CreateOptions();
        Guid reference;
        await using (var context202 = CreateContext(options, 202))
        {
            var foreign = await SeedAssignmentAsync(context202, 202, true, [1,2,3,4,5,6,7,8]);
            reference = foreign.PublicId;
        }
        await using var context101 = CreateContext(options, 101);
        var response = await CreateService(context101, 101).CloseAsync(reference, new CloseStudentTransportRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.Today),
            RowVersion = Convert.ToBase64String(new byte[] { 1,2,3,4,5,6,7,8 })
        });
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        var saved = await context101.Set<StudentTransport>().IgnoreQueryFilters().SingleAsync(x => x.PublicId == reference);
        saved.TenantId.Should().Be(202);
        saved.State.Should().Be(TransportAssignmentState.Active);
        saved.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task My_assignment_requires_current_active_enrollment_and_student_status()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(db, 101, true, [1,2,3,4,5,6,7,8]);
        var student = await db.Set<Student>().SingleAsync();
        student.UserId = 7;
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, CampusId = 1, AcademicYearId = 1,
            AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
            AcademicCurriculumId = 1, RollNo = "7",
            EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = EnrollmentState.Active, IsCurrent = true
        };
        db.Add(enrollment);
        await db.SaveChangesAsync();
        assignment.StudentEnrollmentId = enrollment.Id;
        await db.SaveChangesAsync();
        var service = CreateService(db, 101);
        (await service.GetMyAssignmentAsync()).Data.Should().NotBeNull();
        enrollment.IsCurrent = false;
        await db.SaveChangesAsync();
        (await service.GetMyAssignmentAsync()).Data.Should().BeNull();
        enrollment.IsCurrent = true;
        student.StatusCode = "Transferred";
        await db.SaveChangesAsync();
        (await service.GetMyAssignmentAsync()).Data.Should().BeNull();
    }

    [Fact]
    public async Task Student_cannot_enumerate_transport_routes_or_vehicle_capacity()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var service = CreateService(db, 101, false);
        var routes = await service.GetRoutesAsync();
        var vehicles = await service.GetVehiclesAsync();
        routes.Success.Should().BeFalse();
        routes.StatusCode.Should().Be(403);
        vehicles.Success.Should().BeFalse();
        vehicles.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Tenant_admin_can_read_transport_catalogue()
    {
        await using var db = CreateContext(CreateOptions(), 101);
        var service = CreateService(db, 101);
        (await service.GetRoutesAsync()).Success.Should().BeTrue();
        (await service.GetVehiclesAsync()).Success.Should().BeTrue();
    }

    private static TransportService CreateService(EduOSDbContext context, long tenant, bool canManage = true) => new(
        new GenericRepository<TransportRoute>(context),
        new GenericRepository<RouteStop>(context),
        new GenericRepository<Vehicle>(context),
        new GenericRepository<RouteVehicleAssignment>(context),
        new GenericRepository<StudentTransport>(context),
        new GenericRepository<StudentEnrollment>(context),
        new GenericRepository<Student>(context),
        new TestUnitOfWork(context),
        new TestCurrentUser(tenant, canManage), TimeProvider.System, NullLogger<TransportService>.Instance);

    private static async Task<StudentTransport> SeedAssignmentAsync(EduOSDbContext context, long tenant, bool active, byte[] rowVersion)
    {
        var route = new TransportRoute
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), Name = "Route A", Code = "A",
            DefaultFare = 1500m, IsActive = true
        };
        var vehicle = new Vehicle
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), VehicleNumber = "BUS-01",
            VehicleType = "Bus", Capacity = 30, IsActive = true
        };
        var student = new Student
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), PersonId = 1,
            StudentCode = "STU-" + Guid.NewGuid().ToString("N"), FullName = "Transport Student",
            StatusCode = "Active", AdmissionDate = DateOnly.FromDateTime(DateTime.Today)
        };
        context.AddRange(route, vehicle, student);
        await context.SaveChangesAsync();
        var assignment = new StudentTransport
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, StudentEnrollmentId = 1, VehicleId = vehicle.Id,
            RouteId = route.Id, RouteVehicleAssignmentId = 1,
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)),
            EndDate = active ? null : DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            MonthlyFare = route.DefaultFare,
            State = active ? TransportAssignmentState.Active : TransportAssignmentState.Closed,
            RowVersion = rowVersion
        };
        context.Set<StudentTransport>().Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-service-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUnitOfWork(EduOSDbContext context) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task CommitTransactionAsync() => Task.CompletedTask;
        public Task RollbackTransactionAsync() => Task.CompletedTask;
        public IExecutionStrategy CreateExecutionStrategy() => context.Database.CreateExecutionStrategy();
        public void Dispose() { }
    }

    private sealed class TestCurrentUser(long tenant, bool canManage) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Transport User";
        public string? Email => "transport@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => canManage;
        public IReadOnlyList<string> Roles => canManage ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => canManage && role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
