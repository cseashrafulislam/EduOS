using EduOS.Core.DTOs.Transport;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Student_family_readback_and_export_recheck_live_identity_and_tenant_in_sql()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        long studentId;
        await using (var setup = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var rider = await setup.TransportTripRiders.AsNoTracking().SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.StudentCodeSnapshot == state.StudentCode &&
                x.TransportTrip.PublicId == state.Reference);
            studentId = rider.StudentId;
            var student = await setup.Students.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == studentId);
            student.UserId = state.Fixture.UserId;
            await setup.SaveChangesAsync();
        }
        await using (var writer = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var board = await TripService(writer, state.Fixture).RecordAttendanceAsync(state.Reference,
                new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "Boarded"
                });
            Assert.True(board.Success, board.Message);
        }
        await using (var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            TransportTripService Service(long tenant, long userId) => new(db,
                new FamilyStudent(tenant, userId), TimeProvider.System,
                NullLogger<TransportTripService>.Instance);
            var own = Service(state.Fixture.TenantId, state.Fixture.UserId);
            var date = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
            var rows = await own.GetFamilyAttendanceAsync(date, 1, 20);
            Assert.True(rows.Success, rows.Message);
            Assert.Equal(state.Reference, Assert.Single(rows.Data!.Items).TripReference);
            Assert.Equal("Boarded", rows.Data.Items[0].AttendanceState);
            var history = await own.GetFamilyAttendanceHistoryAsync(state.Reference, state.StudentCode);
            Assert.True(history.Success, history.Message);
            Assert.Equal("Boarded", Assert.Single(history.Data!).EventType);
            var csv = await own.ExportFamilyAttendanceCsvAsync(date);
            Assert.True(csv.Success, csv.Message);
            Assert.Contains(state.StudentCode, csv.Data);
            Assert.Contains("Boarded", csv.Data);

            Assert.Equal(404, (await own.GetFamilyAttendanceHistoryAsync(
                Guid.NewGuid(), state.StudentCode)).StatusCode);
            Assert.Equal(404, (await own.GetFamilyAttendanceHistoryAsync(
                state.Reference, "UNKNOWN-RIDER")).StatusCode);
            var otherUser = Service(state.Fixture.TenantId, state.Fixture.UserId + 99999);
            Assert.Equal(404, (await otherUser.GetFamilyAttendanceHistoryAsync(
                state.Reference, state.StudentCode)).StatusCode);
            Assert.DoesNotContain(state.StudentCode,
                (await otherUser.ExportFamilyAttendanceCsvAsync(date)).Data);
            var otherTenant = Service(long.MaxValue, state.Fixture.UserId);
            Assert.Equal(404, (await otherTenant.GetFamilyAttendanceHistoryAsync(
                state.Reference, state.StudentCode)).StatusCode);
            Assert.DoesNotContain(state.StudentCode,
                (await otherTenant.ExportFamilyAttendanceCsvAsync(date)).Data);
        }
        await using (var revoke = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var student = await revoke.Students.SingleAsync(x =>
                x.TenantId == state.Fixture.TenantId && x.Id == studentId);
            student.UserId = null;
            await revoke.SaveChangesAsync();
        }
        await using (var verify = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var own = new TransportTripService(verify,
                new FamilyStudent(state.Fixture.TenantId, state.Fixture.UserId),
                TimeProvider.System, NullLogger<TransportTripService>.Instance);
            Assert.Equal(404, (await own.GetFamilyAttendanceHistoryAsync(
                state.Reference, state.StudentCode)).StatusCode);
            var csv = await own.ExportFamilyAttendanceCsvAsync(
                DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime));
            Assert.True(csv.Success, csv.Message);
            Assert.DoesNotContain(state.StudentCode, csv.Data);
        }
    }

    private sealed class FamilyStudent(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
