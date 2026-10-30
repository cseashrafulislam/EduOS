using EduOS.Core.DTOs.Transport;
using EduOS.Service.Services.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task Trip_audit_register_preserves_corrections_and_restricts_reason_export_in_sql()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var state = await DispatchedTripAsync(connection);
        const string reason = "Verified operator attendance discrepancy";
        await using (var writer = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId))
        {
            var service = TripService(writer, state.Fixture);
            var recorded = await service.RecordAttendanceAsync(state.Reference,
                new RecordTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode, EventType = "NoShow"
                });
            Assert.True(recorded.Success, recorded.Message);
            var version = Convert.ToBase64String(await writer.TransportTrips.AsNoTracking()
                .Where(x => x.TenantId == state.Fixture.TenantId && x.PublicId == state.Reference)
                .Select(x => x.RowVersion).SingleAsync());
            var completed = await service.CompleteAsync(state.Reference,
                new CompleteTransportTripRequestDto
                { ClientRequestId = Guid.NewGuid(), RowVersion = version });
            Assert.True(completed.Success, completed.Message);
            var correction = await service.CorrectAttendanceAsync(state.Reference,
                new CorrectTransportTripAttendanceRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), StudentCode = state.StudentCode,
                    ExpectedState = "NoShow", CorrectedState = "Dropped", Reason = reason
                });
            Assert.True(correction.Success, correction.Message);
        }
        await using var db = ScopedContext(state.Fixture.Options, state.Fixture.TenantId, state.Fixture.UserId);
        var admin = TripService(db, state.Fixture);
        var page = await admin.GetAttendanceAuditAsync(state.Reference, 1, 1);
        Assert.True(page.Success, page.Message);
        Assert.Equal(2, page.Data!.TotalCount);
        Assert.Equal("NoShow", Assert.Single(page.Data.Items).EventType);
        var second = await admin.GetAttendanceAuditAsync(state.Reference, 2, 1);
        Assert.True(second.Success, second.Message);
        Assert.Equal("Dropped", Assert.Single(second.Data!.Items).EventType);
        var report = await admin.GetAttendanceAuditReportAsync(state.Reference);
        Assert.True(report.Success, report.Message);
        Assert.Equal(2, report.Data!.Events.Count);
        Assert.Equal("NoShow", report.Data.Events[1].CorrectedFrom);
        Assert.Equal(reason, report.Data.Events[1].CorrectionReason);
        Assert.True(report.Data.Events[1].IsCorrection);
        var csv = await admin.ExportAttendanceAuditCsvAsync(state.Reference);
        Assert.True(csv.Success, csv.Message);
        Assert.Contains(reason, csv.Data);
        Assert.Contains(state.StudentCode, csv.Data);
        Assert.Equal(404, (await admin.GetAttendanceAuditReportAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(400, (await admin.GetAttendanceAuditAsync(state.Reference, 1, 101)).StatusCode);

        var manager = new TransportTripService(db,
            new RestrictedTransportManager(state.Fixture.TenantId, state.Fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);
        Assert.Equal(403, (await manager.GetAttendanceAuditReportAsync(state.Reference)).StatusCode);
        Assert.Equal(403, (await manager.ExportAttendanceAuditCsvAsync(state.Reference)).StatusCode);
        var family = new TransportTripService(db,
            new FamilyStudent(state.Fixture.TenantId, state.Fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);
        Assert.Equal(403, (await family.GetAttendanceAuditAsync(state.Reference, 1, 25)).StatusCode);
        Assert.Equal(403, (await family.ExportAttendanceAuditCsvAsync(state.Reference)).StatusCode);
        await using var foreignDb = ScopedContext(state.Fixture.Options, long.MaxValue, state.Fixture.UserId);
        var foreign = new TransportTripService(foreignDb,
            new TenantAdmin(long.MaxValue, state.Fixture.UserId),
            TimeProvider.System, NullLogger<TransportTripService>.Instance);
        Assert.Equal(404, (await foreign.GetAttendanceAuditReportAsync(state.Reference)).StatusCode);
        Assert.Equal(404, (await foreign.ExportAttendanceAuditCsvAsync(state.Reference)).StatusCode);
    }
}
