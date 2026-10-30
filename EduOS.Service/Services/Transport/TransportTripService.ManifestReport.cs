using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>
/// Full, bounded frozen-manifest readback for authorized print and CSV.
/// No live reservation query, student personal details or driver contact information.
/// </summary>
public sealed partial class TransportTripService
{
    public Task<ApiResponse<TransportTripManifestReportDto>> GetManifestReportAsync(
        Guid reference, CancellationToken ct = default) =>
        GetManifestReportCoreAsync(reference, driverOnly: false, ct);

    public Task<ApiResponse<TransportTripManifestReportDto>> GetDriverManifestReportAsync(
        Guid reference, CancellationToken ct = default) =>
        GetManifestReportCoreAsync(reference, driverOnly: true, ct);

    public Task<ApiResponse<string>> ExportDriverRidersCsvAsync(
        Guid reference, CancellationToken ct = default) =>
        ExportManifestCsvAsync(reference, driverOnly: true, ct);

    private async Task<ApiResponse<TransportTripManifestReportDto>> GetManifestReportCoreAsync(
        Guid reference, bool driverOnly, CancellationToken ct)
    {
        if (driverOnly ? !CanDrive : !CanManage)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Transport permission required.", 403);
        if (reference == Guid.Empty)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse("Valid trip required.");

        var tenant = _user.TenantId;
        var trip = await _db.TransportTrips.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                x.PublicId == reference && (x.State == "Dispatched" || x.State == "Completed"))
            .Select(x => new { x.Id, x.CampusId, x.RiderCount })
            .SingleOrDefaultAsync(ct);
        if (trip == null)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Dispatched trip not found.", 404);
        if (driverOnly ? !await CanDriveTripAsync(trip.Id, ct) : !await HasCampusAsync(trip.CampusId, ct))
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Trip assignment or campus access required.", 403);
        if (trip.RiderCount is < 1 or > 500)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Frozen manifest size is invalid.", 409);

        var header = await (from t in _db.TransportTrips.AsNoTracking()
            join route in _db.Routes.AsNoTracking() on t.RouteId equals route.Id
            join vehicle in _db.Vehicles.AsNoTracking() on t.VehicleId equals vehicle.Id
            join campus in _db.Campuses.AsNoTracking() on t.CampusId equals campus.Id
            where t.Id == trip.Id && t.TenantId == tenant && !t.IsDeleted &&
                route.TenantId == tenant && vehicle.TenantId == tenant &&
                campus.TenantId == tenant && !campus.IsDeleted
            select new TransportTripRowDto
            {
                Reference = t.PublicId, CampusId = t.CampusId,
                CampusName = campus.Name, RouteName = route.Name,
                VehicleNumber = vehicle.VehicleNumber, OperatingDate = t.OperatingDate,
                Direction = t.Direction, State = t.State, RiderCount = t.RiderCount,
                RowVersion = Convert.ToBase64String(t.RowVersion)
            }).SingleOrDefaultAsync(ct);
        if (header == null)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Trip header integrity conflict.", 409);

        // One bounded, ordered SQL projection includes all riders and their latest
        // append-only attendance/correction event; never print just the UI's current page.
        var riders = await _db.TransportTripRiders.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id)
            .OrderBy(x => x.StudentCodeSnapshot).ThenBy(x => x.Id)
            .Take(501)
            .Select(x => new TransportTripRiderDto
            {
                StudentCode = x.StudentCodeSnapshot,
                PickupStop = x.PickupStopSnapshot,
                DropStop = x.DropStopSnapshot,
                AttendanceState = _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && !e.IsDeleted &&
                        e.TransportTripRiderId == x.Id)
                    .OrderByDescending(e => e.Sequence)
                    .Select(e => e.EventType).FirstOrDefault() ?? "Awaiting"
            }).ToListAsync(ct);
        if (riders.Count != trip.RiderCount ||
            riders.Select(x => x.StudentCode).Distinct(StringComparer.Ordinal).Count() != riders.Count)
            return ApiResponse<TransportTripManifestReportDto>.ErrorResponse(
                "Frozen manifest integrity conflict.", 409);

        return ApiResponse<TransportTripManifestReportDto>.SuccessResponse(
            new TransportTripManifestReportDto
            {
                Trip = header,
                Riders = riders,
                AwaitingCount = riders.Count(x => x.AttendanceState == "Awaiting"),
                BoardedCount = riders.Count(x => x.AttendanceState == "Boarded"),
                DroppedCount = riders.Count(x => x.AttendanceState == "Dropped"),
                NoShowCount = riders.Count(x => x.AttendanceState == "NoShow"),
                GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime
            });
    }

    private async Task<ApiResponse<string>> ExportManifestCsvAsync(
        Guid reference, bool driverOnly, CancellationToken ct)
    {
        var report = await GetManifestReportCoreAsync(reference, driverOnly, ct);
        if (!report.Success || report.Data == null)
            return ApiResponse<string>.ErrorResponse(report.Message, report.StatusCode);
        var csv = new StringBuilder("\uFEFFStudent code,Pickup stop,Drop stop,Attendance\r\n");
        foreach (var row in report.Data.Riders)
            csv.Append(Escape(row.StudentCode)).Append(',')
                .Append(Escape(row.PickupStop)).Append(',')
                .Append(Escape(row.DropStop)).Append(',')
                .Append(Escape(row.AttendanceState)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
