using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportTripService
{
    // A Staff role alone is never an operational grant. The signed-in user must
    // own the active employee record linked to the frozen dispatched driver and
    // have a current campus assignment for the operating date.
    private bool CanDrive => _user.IsAuthenticated && _user.TenantId > 0 &&
        _user.UserId > 0 && _user.IsInRole("Staff");

    private IQueryable<TransportTrip> DriverTrips()
    {
        var tenant = _user.TenantId;
        var userId = _user.UserId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        return _db.TransportTrips.AsNoTracking().Where(trip =>
            trip.TenantId == tenant && !trip.IsDeleted &&
            trip.OperatingDate == today &&
            (trip.State == "Dispatched" || trip.State == "Completed") &&
            _db.TransportDrivers.AsNoTracking().Any(driver =>
                driver.TenantId == tenant && !driver.IsDeleted && driver.IsActive &&
                driver.Id == trip.TransportDriverId &&
                driver.EmployeeId.HasValue && driver.LicenseExpiresOn.HasValue &&
                driver.LicenseExpiresOn.Value >= today &&
                _db.Employees.AsNoTracking().Any(employee =>
                    employee.TenantId == tenant && !employee.IsDeleted &&
                    employee.Id == driver.EmployeeId.Value && employee.UserId == userId &&
                    employee.State == EmployeeState.Active &&
                    employee.JoiningDate <= today &&
                    (!employee.LeavingDate.HasValue || employee.LeavingDate.Value >= today) &&
                    _db.EmployeeCampusAssignments.AsNoTracking().Any(assignment =>
                        assignment.TenantId == tenant && !assignment.IsDeleted &&
                        assignment.EmployeeId == employee.Id && assignment.CampusId == trip.CampusId &&
                        assignment.IsCurrent && assignment.EffectiveFrom <= today &&
                        (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo.Value >= today)))));
    }

    private Task<bool> CanDriveTripAsync(long tripId, CancellationToken ct) =>
        CanDrive ? DriverTrips().AnyAsync(x => x.Id == tripId, ct) : Task.FromResult(false);

    public async Task<ApiResponse<PagedResult<TransportTripRowDto>>> GetDriverTripsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanDrive)
            return ApiResponse<PagedResult<TransportTripRowDto>>.ErrorResponse("Assigned driver access required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportTripRowDto>>.ErrorResponse("Invalid paging.");
        var tenant = _user.TenantId;
        var query = from trip in DriverTrips()
                    join route in _db.Routes.AsNoTracking() on trip.RouteId equals route.Id
                    join vehicle in _db.Vehicles.AsNoTracking() on trip.VehicleId equals vehicle.Id
                    join campus in _db.Campuses.AsNoTracking() on trip.CampusId equals campus.Id
                    where route.TenantId == tenant && vehicle.TenantId == tenant &&
                          campus.TenantId == tenant && !campus.IsDeleted
                    select new TransportTripRowDto
                    {
                        Reference = trip.PublicId, CampusId = trip.CampusId,
                        CampusName = campus.Name, RouteName = route.Name,
                        VehicleNumber = vehicle.VehicleNumber, OperatingDate = trip.OperatingDate,
                        Direction = trip.Direction, State = trip.State, RiderCount = trip.RiderCount,
                        RowVersion = Convert.ToBase64String(trip.RowVersion)
                    };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Direction).ThenBy(x => x.VehicleNumber)
            .ThenBy(x => x.Reference).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportTripRowDto>>.SuccessResponse(
            new PagedResult<TransportTripRowDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public Task<ApiResponse<PagedResult<TransportTripRiderDto>>> GetDriverRidersAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default) =>
        GetRidersCoreAsync(reference, page, pageSize, driverOnly: true, ct);

    public Task<ApiResponse<TransportTripAttendanceDto>> RecordDriverAttendanceAsync(
        Guid reference, RecordTransportTripAttendanceRequestDto request, CancellationToken ct = default) =>
        RecordAttendanceCoreAsync(reference, request, driverOnly: true, ct);
}
