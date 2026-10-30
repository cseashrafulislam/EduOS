using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
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

public sealed class TransportTripAttendanceTests
{
    [Fact]
    public async Task Attendance_is_append_only_idempotent_and_monotonic()
    {
        await using var db = Context(101);
        var (trip, rider) = await SeedAsync(db);
        var service = Service(db, new User(101, "TenantAdmin"));
        var beforeBoard = await service.RecordAttendanceAsync(trip.PublicId,
            new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = Guid.NewGuid(), StudentCode = rider.StudentCodeSnapshot, EventType = "Dropped" });
        beforeBoard.StatusCode.Should().Be(409);
        var board = new RecordTransportTripAttendanceRequestDto
        { ClientRequestId = Guid.NewGuid(), StudentCode = rider.StudentCodeSnapshot, EventType = "Boarded" };
        var first = await service.RecordAttendanceAsync(trip.PublicId, board);
        first.Success.Should().BeTrue(first.Message);
        (await service.RecordAttendanceAsync(trip.PublicId, board)).Success.Should().BeTrue();
        (await service.RecordAttendanceAsync(trip.PublicId,
            new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = board.ClientRequestId, StudentCode = rider.StudentCodeSnapshot, EventType = "NoShow" }))
            .StatusCode.Should().Be(409);
        (await service.RecordAttendanceAsync(trip.PublicId,
            new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = Guid.NewGuid(), StudentCode = rider.StudentCodeSnapshot, EventType = "NoShow" }))
            .StatusCode.Should().Be(409);
        (await service.RecordAttendanceAsync(trip.PublicId,
            new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = Guid.NewGuid(), StudentCode = rider.StudentCodeSnapshot, EventType = "Dropped" }))
            .Success.Should().BeTrue();
        db.TransportTripAttendanceEvents.Count().Should().Be(2);
        var manifest = await service.GetRidersAsync(trip.PublicId, 1, 25);
        manifest.Data!.Items.Single().AttendanceState.Should().Be("Dropped");
        var ev = await db.TransportTripAttendanceEvents.FirstAsync();
        ev.EventType = "NoShow";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Family_readback_requires_live_guardian_link_and_tenant_ownership()
    {
        await using var db = Context(101);
        var (trip, rider) = await SeedAsync(db);
        db.Students.Add(new Student
        {
            Id = rider.StudentId, TenantId = 101, UserId = 70,
            StudentCode = rider.StudentCodeSnapshot, FullName = "Linked learner"
        });
        db.Guardians.Add(new Guardian { Id = 80, TenantId = 101, UserId = 42, FullName = "Linked guardian" });
        db.StudentGuardians.Add(new StudentGuardian
        {
            TenantId = 101, StudentId = rider.StudentId, GuardianId = 80,
            RelationCode = "Parent", IsAuthorizedPickup = true
        });
        await db.SaveChangesAsync();
        var guardianService = Service(db, new User(101, "Guardian"));
        var date = trip.OperatingDate;
        (await guardianService.GetFamilyAttendanceAsync(date, 1, 20)).Data!.TotalCount.Should().Be(1);
        (await Service(db, new User(202, "Guardian")).GetFamilyAttendanceAsync(date, 1, 20))
            .Data!.TotalCount.Should().Be(0);
        var link = await db.StudentGuardians.SingleAsync();
        link.IsAuthorizedPickup = false;
        await db.SaveChangesAsync();
        (await guardianService.GetFamilyAttendanceAsync(date, 1, 20)).Data!.TotalCount.Should().Be(0);
        (await Service(db, new User(101, "TransportManager")).GetFamilyAttendanceAsync(date, 1, 20))
            .StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task Family_history_and_csv_recheck_live_guardian_link_and_redact_staff_correction_reason()
    {
        await using var db = Context(101);
        var (trip, rider) = await SeedAsync(db);
        db.Students.Add(new Student
        {
            Id = rider.StudentId, TenantId = 101, UserId = 70,
            StudentCode = rider.StudentCodeSnapshot, FullName = "Linked learner"
        });
        db.Guardians.Add(new Guardian { Id = 80, TenantId = 101, UserId = 42, FullName = "Linked guardian" });
        db.StudentGuardians.Add(new StudentGuardian
        {
            TenantId = 101, StudentId = rider.StudentId, GuardianId = 80,
            RelationCode = "Parent", IsAuthorizedPickup = true
        });
        var board = new TransportTripAttendanceEvent
        {
            TenantId = 101, TransportTripRiderId = rider.Id, ClientRequestId = Guid.NewGuid(),
            Sequence = 1, EventType = "Boarded", OccurredAt = DateTime.UtcNow
        };
        db.TransportTripAttendanceEvents.Add(board);
        await db.SaveChangesAsync();
        var drop = new TransportTripAttendanceEvent
        {
            TenantId = 101, TransportTripRiderId = rider.Id, ClientRequestId = Guid.NewGuid(),
            Sequence = 2, EventType = "Dropped", OccurredAt = DateTime.UtcNow
        };
        db.TransportTripAttendanceEvents.Add(drop);
        await db.SaveChangesAsync();
        trip.State = "Completed";
        trip.CompletionRequestId = Guid.NewGuid();
        trip.CompletedAt = DateTime.UtcNow;
        trip.CompletedBy = 42;
        await db.SaveChangesAsync();
        db.TransportTripAttendanceEvents.Add(new TransportTripAttendanceEvent
        {
            TenantId = 101, TransportTripRiderId = rider.Id, ClientRequestId = Guid.NewGuid(),
            Sequence = 3, EventType = "NoShow", OccurredAt = DateTime.UtcNow,
            CorrectsEventId = drop.Id, CorrectionReason = "Internal sensitive staff-only explanation"
        });
        await db.SaveChangesAsync();

        var guardian = Service(db, new User(101, "Guardian"));
        var history = await guardian.GetFamilyAttendanceHistoryAsync(trip.PublicId, rider.StudentCodeSnapshot);
        history.Success.Should().BeTrue(history.Message);
        history.Data!.Count.Should().Be(3);
        history.Data.Last().IsCorrection.Should().BeTrue();
        history.Data.Last().CorrectionReason.Should().BeNull();
        var list = await guardian.GetFamilyAttendanceAsync(trip.OperatingDate, 1, 20);
        list.Data!.Items.Single().TripReference.Should().Be(trip.PublicId);
        list.Data.Items.Single().AttendanceState.Should().Be("NoShow");
        var csv = await guardian.ExportFamilyAttendanceCsvAsync(trip.OperatingDate);
        csv.Success.Should().BeTrue(csv.Message);
        csv.Data.Should().Contain(rider.StudentCodeSnapshot).And.Contain("NoShow");
        csv.Data.Should().NotContain("Internal sensitive");

        (await guardian.GetFamilyAttendanceHistoryAsync(Guid.NewGuid(), rider.StudentCodeSnapshot))
            .StatusCode.Should().Be(404);
        (await guardian.GetFamilyAttendanceHistoryAsync(trip.PublicId, "UNKNOWN"))
            .StatusCode.Should().Be(404);
        (await Service(db, new User(202, "Guardian"))
            .GetFamilyAttendanceHistoryAsync(trip.PublicId, rider.StudentCodeSnapshot))
            .StatusCode.Should().Be(404);
        (await Service(db, new User(101, "TransportManager"))
            .ExportFamilyAttendanceCsvAsync(trip.OperatingDate)).StatusCode.Should().Be(403);

        var link = await db.StudentGuardians.SingleAsync();
        link.IsAuthorizedPickup = false;
        await db.SaveChangesAsync();
        (await guardian.GetFamilyAttendanceHistoryAsync(trip.PublicId, rider.StudentCodeSnapshot))
            .StatusCode.Should().Be(404);
        (await guardian.ExportFamilyAttendanceCsvAsync(trip.OperatingDate))
            .Data.Should().NotContain(rider.StudentCodeSnapshot);
    }

    [Fact]
    public void Attendance_model_has_tenant_composite_fk_and_unique_sequence_and_request()
    {
        using var db = Context(101);
        var entity = db.Model.FindEntityType(typeof(TransportTripAttendanceEvent));
        entity.Should().NotBeNull();
        entity!.GetForeignKeys().Should().Contain(x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "TransportTripRiderId" }));
        entity.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "TransportTripRiderId", "Sequence" }));
        entity.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "TenantId", "ClientRequestId" }));
    }

    private static TransportTripService Service(EduOSDbContext db, ICurrentUserService user) =>
        new(db, user, TimeProvider.System, NullLogger<TransportTripService>.Instance);

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-attendance-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<(TransportTrip trip, TransportTripRider rider)> SeedAsync(EduOSDbContext db)
    {
        var campus = new Campus { TenantId = 101, Name = "Transport campus", Code = "ATT", IsActive = true };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var route = new Route { TenantId = 101, CampusId = campus.Id, Name = "East", Code = "EAST", IsActive = true };
        var vehicle = new Vehicle { TenantId = 101, CampusId = campus.Id,
            VehicleNumber = "ATT-101", Capacity = 20, IsActive = true };
        db.AddRange(route, vehicle);
        await db.SaveChangesAsync();
        var trip = new TransportTrip
        {
            TenantId = 101, CampusId = campus.Id, RouteId = route.Id, VehicleId = vehicle.Id,
            TransportDriverId = 91, ClientRequestId = Guid.NewGuid(), DispatchRequestId = Guid.NewGuid(),
            OperatingDate = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            Direction = "Pickup", State = "Dispatched", RiderCount = 1,
            DispatchedAt = DateTime.UtcNow, DispatchedBy = 42
        };
        db.TransportTrips.Add(trip);
        await db.SaveChangesAsync();
        var rider = new TransportTripRider
        {
            TenantId = 101, TransportTripId = trip.Id, StudentTransportId = 12,
            StudentId = 70, StudentEnrollmentId = 15,
            StudentCodeSnapshot = "S-70", PickupStopId = 1, DropStopId = 2,
            PickupStopSnapshot = "East", DropStopSnapshot = "West"
        };
        db.TransportTripRiders.Add(rider);
        await db.SaveChangesAsync();
        return (trip, rider);
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
