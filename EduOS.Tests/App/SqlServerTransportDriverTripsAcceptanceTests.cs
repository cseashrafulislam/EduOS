using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Staff_driver_can_operate_only_verified_assigned_trip_and_loses_access_on_campus_revocation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        long assignmentId;
        await using (var setup = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var trip = await setup.TransportTrips.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference);
            var driver = await setup.TransportDrivers.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == trip.TransportDriverId);
            var person = new Person { FullName = "Assigned driver identity" };
            var designation = new Designation
            {
                TenantId = state.Fixture.TenantId, Name = "Driver", Code = "DRV-" + Guid.NewGuid().ToString("N")[..12]
            };
            setup.AddRange(person, designation);
            await setup.SaveChangesAsync();
            var employee = new Employee
            {
                TenantId = state.Fixture.TenantId, PersonId = person.Id,
                UserId = state.Fixture.UserId, DesignationId = designation.Id,
                EmployeeCode = "DRV-" + Guid.NewGuid().ToString("N")[..12],
                FullName = "Assigned driver identity", JoiningDate = today.AddDays(-10),
                State = EmployeeState.Active
            };
            setup.Employees.Add(employee);
            await setup.SaveChangesAsync();
            driver.EmployeeId = employee.Id;
            var campus = new EmployeeCampusAssignment
            {
                TenantId = state.Fixture.TenantId, EmployeeId = employee.Id,
                CampusId = trip.CampusId, EffectiveFrom = today.AddDays(-10),
                IsCurrent = true
            };
            setup.EmployeeCampusAssignments.Add(campus);
            await setup.SaveChangesAsync();
            assignmentId = campus.Id;
        }

        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var service = new TransportTripService(db,
                new StaffDriver(state.Fixture.TenantId, state.Fixture.UserId),
                TimeProvider.System, NullLogger<TransportTripService>.Instance);
            var trips = await service.GetDriverTripsAsync(1, 20);
            Assert.True(trips.Success, trips.Message);
            Assert.Equal(state.Reference, Assert.Single(trips.Data!.Items).Reference);
            var riders = await service.GetDriverRidersAsync(state.Reference, 1, 25);
            Assert.True(riders.Success, riders.Message);
            Assert.Equal(state.StudentCode, Assert.Single(riders.Data!.Items).StudentCode);
            var key = Guid.NewGuid();
            var request = new RecordTransportTripAttendanceRequestDto
            { ClientRequestId = key, StudentCode = state.StudentCode, EventType = "Boarded" };
            Assert.True((await service.RecordDriverAttendanceAsync(state.Reference, request)).Success);
            Assert.True((await service.RecordDriverAttendanceAsync(state.Reference, request)).Success);
            Assert.Equal(404, (await service.GetDriverRidersAsync(Guid.NewGuid(), 1, 25)).StatusCode);
            Assert.Equal(404, (await service.RecordDriverAttendanceAsync(Guid.NewGuid(),
                new RecordTransportTripAttendanceRequestDto
                { ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "NoShow" })).StatusCode);
        }

        await using (var foreignDb = ScopedContext(state.Fixture.Options, long.MaxValue, state.Fixture.UserId))
        {
            var foreign = new TransportTripService(foreignDb,
                new StaffDriver(long.MaxValue, state.Fixture.UserId),
                TimeProvider.System, NullLogger<TransportTripService>.Instance);
            Assert.Empty((await foreign.GetDriverTripsAsync(1, 20)).Data!.Items);
            Assert.Equal(404, (await foreign.GetDriverRidersAsync(state.Reference, 1, 25)).StatusCode);
        }
        await using (var revoke = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var assignment = await revoke.EmployeeCampusAssignments.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == assignmentId);
            assignment.IsCurrent = false;
            await revoke.SaveChangesAsync();
        }
        await using (var revokedDb = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var revoked = new TransportTripService(revokedDb,
                new StaffDriver(state.Fixture.TenantId, state.Fixture.UserId),
                TimeProvider.System, NullLogger<TransportTripService>.Instance);
            Assert.Empty((await revoked.GetDriverTripsAsync(1, 20)).Data!.Items);
            Assert.Equal(403, (await revoked.GetDriverRidersAsync(state.Reference, 1, 25)).StatusCode);
            Assert.Equal(403, (await revoked.RecordDriverAttendanceAsync(state.Reference,
                new RecordTransportTripAttendanceRequestDto
                { ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "Dropped" })).StatusCode);
        }
    }

    private sealed class StaffDriver(long tenant, long user) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Staff"];
        public bool IsInRole(string role) => role == "Staff";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
