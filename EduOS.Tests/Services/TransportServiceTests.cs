using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        var assignment = await SeedAssignmentAsync(context, 101, true, [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(assignment.PublicId, new CloseTransportDto
        {
            EndDate = DateTime.Today,
            RowVersion = Convert.ToBase64String(assignment.RowVersion)
        });

        response.Success.Should().BeTrue();
        response.Data!.IsActive.Should().BeFalse();
        var saved = await context.StudentTransports.IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.IsActive.Should().BeFalse();
        saved.EndDate.Should().Be(DateTime.Today);
    }

    [Fact]
    public async Task Close_with_stale_row_version_is_rejected_without_mutation()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, true, [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(assignment.PublicId, new CloseTransportDto
        {
            EndDate = DateTime.Today,
            RowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
        });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        var saved = await context.StudentTransports.IgnoreQueryFilters().SingleAsync(x => x.Id == assignment.Id);
        saved.IsActive.Should().BeTrue();
        saved.EndDate.Should().BeNull();
    }

    [Fact]
    public async Task Closing_an_already_closed_assignment_remains_idempotent()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var assignment = await SeedAssignmentAsync(context, 101, false, [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(assignment.PublicId, new CloseTransportDto
        {
            EndDate = DateTime.Today,
            RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9])
        });

        response.Success.Should().BeTrue();
        response.Data!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Close_cannot_mutate_assignment_from_another_tenant()
    {
        var options = CreateOptions();
        Guid foreignReference;
        await using (var seedContext = CreateContext(options, 202))
        {
            var foreign = await SeedAssignmentAsync(seedContext, 202, true, [1, 2, 3, 4, 5, 6, 7, 8]);
            foreignReference = foreign.PublicId;
        }

        await using var context = CreateContext(options, 101);
        var service = CreateService(context, 101);
        var response = await service.CloseAsync(foreignReference, new CloseTransportDto
        {
            EndDate = DateTime.Today,
            RowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8])
        });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        var saved = await context.StudentTransports.IgnoreQueryFilters().SingleAsync(x => x.PublicId == foreignReference);
        saved.TenantId.Should().Be(202);
        saved.IsActive.Should().BeTrue();
        saved.EndDate.Should().BeNull();
    }

    private static TransportService CreateService(EduOSDbContext context, long tenantId) => new(
        new GenericRepository<TransportRoute>(context),
        new GenericRepository<Vehicle>(context),
        new GenericRepository<StudentTransport>(context),
        new GenericRepository<Student>(context),
        context,
        new TestCurrentUser(tenantId),
        TimeProvider.System,
        NullLogger<TransportService>.Instance);

    private static async Task<StudentTransport> SeedAssignmentAsync(EduOSDbContext context, long tenantId, bool isActive, byte[] rowVersion)
    {
        var route = new TransportRoute { TenantId = tenantId, PublicId = Guid.NewGuid(), Name = "Route A", Distance = 10, Fare = 1500, IsActive = true };
        context.Routes.Add(route);
        await context.SaveChangesAsync();

        var vehicle = new Vehicle { TenantId = tenantId, PublicId = Guid.NewGuid(), VehicleNo = "BUS-01", Type = "Bus", Capacity = 30, RouteId = route.Id, Route = route, IsActive = true };
        var student = new Student { TenantId = tenantId, PublicId = Guid.NewGuid(), StudentCode = $"STU-{Guid.NewGuid():N}", FullName = "Transport Student", Status = "Active", IsActive = true };
        context.AddRange(vehicle, student);
        await context.SaveChangesAsync();

        var assignment = new StudentTransport
        {
            TenantId = tenantId,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id,
            Student = student,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            RouteId = route.Id,
            Route = route,
            StartDate = DateTime.Today.AddDays(-10),
            EndDate = isActive ? null : DateTime.Today.AddDays(-1),
            MonthlyFare = route.Fare,
            IsActive = isActive,
            RowVersion = rowVersion
        };
        context.StudentTransports.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"transport-service-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Transport User";
        public string? Email => "transport@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}