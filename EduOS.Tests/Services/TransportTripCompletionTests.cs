using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportTripCompletionTests
{
    [Theory]
    [InlineData("NoShow")]
    [InlineData("Dropped")]
    public async Task Completion_requires_terminal_rider_attendance_and_is_idempotent(string terminal)
    {
        await using var db = Context();
        var trip = await SeedAsync(db);
        var service = Service(db, 101, "TenantAdmin");
        var version = Convert.ToBase64String(trip.RowVersion);
        var request = new CompleteTransportTripRequestDto { ClientRequestId = Guid.NewGuid(), RowVersion = version };
        (await service.CompleteAsync(trip.PublicId, request)).StatusCode.Should().Be(409);
        if (terminal == "Dropped")
        {
            (await service.RecordAttendanceAsync(trip.PublicId, new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = Guid.NewGuid(), StudentCode = "S-101", EventType = "Boarded" })).Success.Should().BeTrue();
            (await service.CompleteAsync(trip.PublicId, request)).StatusCode.Should().Be(409);
        }
        (await service.RecordAttendanceAsync(trip.PublicId, new RecordTransportTripAttendanceRequestDto
        { ClientRequestId = Guid.NewGuid(), StudentCode = "S-101", EventType = terminal })).Success.Should().BeTrue();
        var done = await service.CompleteAsync(trip.PublicId, request);
        done.Success.Should().BeTrue(done.Message);
        done.Data!.State.Should().Be("Completed");
        (await service.CompleteAsync(trip.PublicId, request)).Success.Should().BeTrue();
        (await service.CompleteAsync(trip.PublicId, new CompleteTransportTripRequestDto
        { ClientRequestId = Guid.NewGuid(), RowVersion = version })).StatusCode.Should().Be(409);
        (await service.RecordAttendanceAsync(trip.PublicId, new RecordTransportTripAttendanceRequestDto
        { ClientRequestId = Guid.NewGuid(), StudentCode = "S-101", EventType = "Boarded" })).StatusCode.Should().Be(409);
        (await service.GetRidersAsync(trip.PublicId, 1, 25)).Data!.Items.Single()
            .AttendanceState.Should().Be(terminal);
        var persisted = await db.TransportTrips.SingleAsync();
        persisted.State.Should().Be("Completed");
        persisted.CompletedAt.Should().NotBeNull();
        persisted.CompletedBy.Should().Be(42);
        persisted.CompletionRequestId.Should().Be(request.ClientRequestId);
        persisted.VehicleId = 999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Completion_rejects_wrong_tenant_role_and_stale_rowversion()
    {
        await using var db = Context();
        var trip = await SeedAsync(db);
        var version = Convert.ToBase64String(trip.RowVersion);
        var request = new CompleteTransportTripRequestDto { ClientRequestId = Guid.NewGuid(), RowVersion = version };
        (await Service(db, 202, "TenantAdmin").CompleteAsync(trip.PublicId, request)).StatusCode.Should().Be(404);
        (await Service(db, 101, "Student").CompleteAsync(trip.PublicId, request)).StatusCode.Should().Be(403);
        (await Service(db, 101, "TenantAdmin").CompleteAsync(trip.PublicId,
            new CompleteTransportTripRequestDto { ClientRequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(new byte[8]) }))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public void Completion_schema_has_tenant_scoped_unique_request_and_concurrency_token()
    {
        using var db = Context();
        var type = db.Model.FindEntityType(typeof(TransportTrip));
        type.Should().NotBeNull();
        type!.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "CompletionRequestId" }));
        type.FindProperty(nameof(TransportTrip.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    private static TransportTripService Service(EduOSDbContext db, long tenant, string role) =>
        new(db, new User(tenant, role), TimeProvider.System, NullLogger<TransportTripService>.Instance);

    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-trip-completion-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", "101")
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<TransportTrip> SeedAsync(EduOSDbContext db)
    {
        var campus = new Campus { TenantId = 101, Name = "Campus", Code = "TC", IsActive = true };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var route = new Route { TenantId = 101, CampusId = campus.Id, Name = "Route", Code = "TC-R", IsActive = true };
        var vehicle = new Vehicle { TenantId = 101, CampusId = campus.Id, VehicleNumber = "TC-101", Capacity = 10, IsActive = true };
        db.AddRange(route, vehicle);
        await db.SaveChangesAsync();
        var trip = new TransportTrip
        {
            TenantId = 101, CampusId = campus.Id, RouteId = route.Id, VehicleId = vehicle.Id,
            TransportDriverId = 42, ClientRequestId = Guid.NewGuid(), DispatchRequestId = Guid.NewGuid(),
            OperatingDate = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            Direction = "Pickup", State = "Dispatched", RiderCount = 1,
            DispatchedAt = DateTime.UtcNow, DispatchedBy = 42,
            RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };
        db.TransportTrips.Add(trip);
        await db.SaveChangesAsync();
        db.TransportTripRiders.Add(new TransportTripRider
        {
            TenantId = 101, TransportTripId = trip.Id, StudentTransportId = 100,
            StudentId = 101, StudentEnrollmentId = 101,
            StudentCodeSnapshot = "S-101", PickupStopId = 1, DropStopId = 2,
            PickupStopSnapshot = "Pickup", DropStopSnapshot = "Drop"
        });
        await db.SaveChangesAsync();
        return trip;
    }

    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 42;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
