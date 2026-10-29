using Microsoft.Data.SqlClient;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>Real persisted service slots and frozen dispatch-time rider snapshots; not live tracking.</summary>
public sealed partial class TransportTripService : ITransportTripService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<TransportTripService> _logger;
    public TransportTripService(EduOSDbContext db, ICurrentUserService user,
        TimeProvider clock, ILogger<TransportTripService> logger)
    {
        _db = db; _user = user; _clock = clock; _logger = logger;
    }

    // Provider and EF execution strategies can wrap SQL concurrency exceptions.
    // Classify only known retryable SQL conflict numbers; do not mask unrelated errors.
    private static bool IsRetryableTripSqlConflict(Exception exception, int depth = 0)
    {
        if (depth >= 8) return false;
        if (exception is SqlException sql && sql.Number is 1205 or 1222 or 3960)
            return true;
        if (exception is AggregateException aggregate)
            return aggregate.InnerExceptions.Any(inner => IsRetryableTripSqlConflict(inner, depth + 1));
        return exception.InnerException != null &&
            IsRetryableTripSqlConflict(exception.InnerException, depth + 1);
    }

    private bool CanManage => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("TransportManager"));

    private IQueryable<long> AllowedCampuses() => _db.UserCampusAccesses.AsNoTracking()
        .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
            x.UserId == _user.UserId && x.IsActive).Select(x => x.CampusId);

    private async Task<bool> HasCampusAsync(long campusId, CancellationToken ct) =>
        _user.IsTenantAdmin || await AllowedCampuses().AnyAsync(x => x == campusId, ct);

    private IQueryable<TransportTripRowDto> Query()
    {
        var tenant = _user.TenantId;
        var allowed = AllowedCampuses();
        return from trip in _db.TransportTrips.AsNoTracking()
               join route in _db.Routes.AsNoTracking() on trip.RouteId equals route.Id
               join vehicle in _db.Vehicles.AsNoTracking() on trip.VehicleId equals vehicle.Id
               join campus in _db.Campuses.AsNoTracking() on trip.CampusId equals campus.Id
               where trip.TenantId == tenant && !trip.IsDeleted &&
                     route.TenantId == tenant && vehicle.TenantId == tenant &&
                     campus.TenantId == tenant && !campus.IsDeleted &&
                     (_user.IsTenantAdmin || allowed.Contains(trip.CampusId))
               select new TransportTripRowDto
               {
                   Reference = trip.PublicId, CampusId = trip.CampusId,
                   CampusName = campus.Name, RouteName = route.Name,
                   VehicleNumber = vehicle.VehicleNumber, OperatingDate = trip.OperatingDate,
                   Direction = trip.Direction, State = trip.State, RiderCount = trip.RiderCount,
                   RowVersion = Convert.ToBase64String(trip.RowVersion)
               };
    }

    public async Task<ApiResponse<TransportTripRowDto>> PlanAsync(
        PlanTransportTripRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportTripRowDto>.ErrorResponse("Transport permission required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.RouteReference == Guid.Empty || request.VehicleReference == Guid.Empty ||
            request.Direction is not ("Pickup" or "Drop") ||
            request.OperatingDate < today || request.OperatingDate > today.AddDays(30))
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Valid date, route, vehicle, direction and request key are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var prior = await _db.TransportTrips.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (prior != null)
                {
                    if (!await HasCampusAsync(prior.CampusId, token))
                        return ApiResponse<TransportTripRowDto>.ErrorResponse("Campus access required.", 403);
                    var same = prior.OperatingDate == request.OperatingDate && prior.Direction == request.Direction &&
                        await _db.Routes.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                            x.Id == prior.RouteId && x.PublicId == request.RouteReference, token) &&
                        await _db.Vehicles.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                            x.Id == prior.VehicleId && x.PublicId == request.VehicleReference, token);
                    if (!same) return ApiResponse<TransportTripRowDto>.ErrorResponse("Request key reused with different trip.", 409);
                    var replay = await Query().FirstOrDefaultAsync(x => x.Reference == prior.PublicId, token);
                    return replay == null ? ApiResponse<TransportTripRowDto>.ErrorResponse("Trip is unavailable.", 403)
                        : ApiResponse<TransportTripRowDto>.SuccessResponse(replay, "Existing trip returned.");
                }
                var pair = await (from route in _db.Routes.AsNoTracking()
                    join vehicle in _db.Vehicles.AsNoTracking() on route.CampusId equals vehicle.CampusId
                    join campus in _db.Campuses.AsNoTracking() on route.CampusId equals (long?)campus.Id
                    where route.TenantId == tenant && vehicle.TenantId == tenant &&
                        campus.TenantId == tenant && !route.IsDeleted && route.IsActive &&
                        !vehicle.IsDeleted && vehicle.IsActive && !campus.IsDeleted && campus.IsActive &&
                        route.PublicId == request.RouteReference && vehicle.PublicId == request.VehicleReference &&
                        route.CampusId.HasValue && vehicle.Capacity > 0
                    select new { RouteId = route.Id, VehicleId = vehicle.Id, CampusId = campus.Id })
                    .FirstOrDefaultAsync(token);
                if (pair == null) return ApiResponse<TransportTripRowDto>.ErrorResponse("Active route and vehicle on the same campus required.", 404);
                if (!await HasCampusAsync(pair.CampusId, token))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Campus access required.", 403);
                var linked = await _db.RouteVehicleAssignments.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.RouteId == pair.RouteId && x.VehicleId == pair.VehicleId &&
                    x.EffectiveFrom <= request.OperatingDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.OperatingDate), token);
                if (!linked) return ApiResponse<TransportTripRowDto>.ErrorResponse("Route-vehicle coverage is not active on this date.", 409);
                var trip = new TransportTrip
                {
                    TenantId = tenant, CampusId = pair.CampusId, RouteId = pair.RouteId,
                    VehicleId = pair.VehicleId, OperatingDate = request.OperatingDate,
                    Direction = request.Direction, ClientRequestId = request.ClientRequestId,
                    State = "Planned", CreatedBy = _user.UserId
                };
                _db.TransportTrips.Add(trip);
                await _db.SaveChangesAsync(token);
                var row = await Query().FirstAsync(x => x.Reference == trip.PublicId, token);
                return new ApiResponse<TransportTripRowDto>
                { Success = true, StatusCode = 201, Data = row, Message = "Trip planned." };
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport trip plan conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("A trip already occupies this vehicle/date/direction.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport trip SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Concurrent trip operation; reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport trip plan failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip could not be planned.", 500);
        }
    }

    public async Task<ApiResponse<TransportTripRowDto>> DispatchAsync(
        Guid reference, DispatchTransportTripRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<TransportTripRowDto>.ErrorResponse("Transport permission required.", 403);
        byte[] version;
        try { version = Convert.FromBase64String(request?.RowVersion ?? ""); }
        catch (FormatException) { return ApiResponse<TransportTripRowDto>.ErrorResponse("Invalid row version."); }
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty || version.Length != 8)
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Valid trip, request key and row version required.");
        var tenant = _user.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var trip = await _db.TransportTrips.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (trip == null) return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip not found.", 404);
                if (!await HasCampusAsync(trip.CampusId, token))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Campus access required.", 403);
                if (trip.State == "Dispatched")
                {
                    if (trip.DispatchRequestId != request.ClientRequestId)
                        return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip already dispatched.", 409);
                    var replay = await Query().FirstAsync(x => x.Reference == reference, token);
                    return ApiResponse<TransportTripRowDto>.SuccessResponse(replay, "Existing dispatch returned.");
                }
                if (trip.State != "Planned" || trip.OperatingDate != today)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Only today's planned trips may dispatch.", 409);
                if (!trip.RowVersion.SequenceEqual(version))
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip changed; reload before dispatch.", 409);
                var vehicle = await _db.Vehicles.AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.Id == trip.VehicleId && !x.IsDeleted &&
                    x.IsActive && x.CampusId == trip.CampusId && x.Capacity > 0)
                    .Select(x => new { x.Capacity }).FirstOrDefaultAsync(token);
                var routeOk = await _db.Routes.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == trip.RouteId && !x.IsDeleted &&
                    x.IsActive && x.CampusId == trip.CampusId, token);
                var campusOk = await _db.Campuses.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == trip.CampusId && !x.IsDeleted && x.IsActive, token);
                if (vehicle == null || !routeOk || !campusOk)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Route, vehicle or campus is no longer active.", 409);
                var linkCount = await _db.RouteVehicleAssignments.AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.RouteId == trip.RouteId && x.VehicleId == trip.VehicleId &&
                    x.EffectiveFrom <= today && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token);
                if (linkCount != 1) return ApiResponse<TransportTripRowDto>.ErrorResponse("Route-vehicle coverage conflict.", 409);
                var driverLinks = await (from link in _db.VehicleDriverAssignments.AsNoTracking()
                    join driver in _db.TransportDrivers.AsNoTracking() on link.TransportDriverId equals driver.Id
                    where link.TenantId == tenant && driver.TenantId == tenant &&
                        !link.IsDeleted && link.IsCurrent && link.VehicleId == trip.VehicleId &&
                        link.EffectiveFrom <= today && (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= today)
                    select new
                    {
                        driver.Id,
                        Eligible = !driver.IsDeleted && driver.IsActive &&
                            driver.LicenseExpiresOn.HasValue && driver.LicenseExpiresOn.Value >= today
                    }).Take(2).ToListAsync(token);
                if (driverLinks.Count != 1 || !driverLinks[0].Eligible)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Exactly one eligible driver is required.", 409);
                var safetyHold = await _db.TransportVehicleCases.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == trip.VehicleId &&
                    x.State == "Open" && x.OccurredOn <= today, token);
                if (safetyHold) return ApiResponse<TransportTripRowDto>.ErrorResponse("Vehicle has an unresolved safety case.", 409);
                var assignments = _db.StudentTransports.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == trip.VehicleId &&
                    x.State == TransportAssignmentState.Active && x.StartDate <= today &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= today));
                var count = await assignments.CountAsync(token);
                if (count < 1 || count > vehicle.Capacity || count > 500)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("No riders or vehicle capacity exceeded.", 409);
                var rows = await (from a in assignments
                    join enrollment in _db.StudentEnrollments.AsNoTracking() on a.StudentEnrollmentId equals enrollment.Id
                    join student in _db.Students.AsNoTracking() on a.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant
                    select new
                    {
                        Assignment = a.Id, a.RouteId, a.StudentId, a.StudentEnrollmentId,
                        a.PickupStopId, a.DropStopId, student.StudentCode,
                        Ready = !enrollment.IsDeleted && enrollment.IsCurrent &&
                            enrollment.State == EnrollmentState.Active &&
                            enrollment.StudentId == a.StudentId && enrollment.CampusId == trip.CampusId &&
                            !student.IsDeleted && student.StatusCode == "Active",
                        Pickup = _db.RouteStops.Where(x => x.TenantId == tenant && !x.IsDeleted &&
                            x.IsActive && x.RouteId == trip.RouteId && x.Id == a.PickupStopId)
                            .Select(x => x.Name).FirstOrDefault(),
                        Drop = _db.RouteStops.Where(x => x.TenantId == tenant && !x.IsDeleted &&
                            x.IsActive && x.RouteId == trip.RouteId && x.Id == a.DropStopId)
                            .Select(x => x.Name).FirstOrDefault()
                    }).Take(501).ToListAsync(token);
                if (rows.Count != count || rows.Any(x => x.RouteId != trip.RouteId ||
                    !x.Ready || !x.PickupStopId.HasValue || !x.DropStopId.HasValue ||
                    string.IsNullOrWhiteSpace(x.Pickup) || string.IsNullOrWhiteSpace(x.Drop)) ||
                    rows.Select(x => x.StudentId).Distinct().Count() != count)
                    return ApiResponse<TransportTripRowDto>.ErrorResponse("Reservation, enrollment or stop changed; review manifest.", 409);
                var conflict = await _db.TransportTrips.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != trip.Id &&
                    x.OperatingDate == today && x.Direction == trip.Direction &&
                    x.TransportDriverId == driverLinks[0].Id && x.State == "Dispatched", token);
                if (conflict) return ApiResponse<TransportTripRowDto>.ErrorResponse("Driver already dispatched on another vehicle.", 409);
                _db.Entry(trip).Property(x => x.RowVersion).OriginalValue = version;
                trip.State = "Dispatched";
                trip.DispatchRequestId = request.ClientRequestId;
                trip.TransportDriverId = driverLinks[0].Id;
                trip.DispatchedAt = _clock.GetUtcNow().UtcDateTime;
                trip.DispatchedBy = _user.UserId;
                trip.RiderCount = count;
                _db.TransportTripRiders.AddRange(rows.Select(x => new TransportTripRider
                {
                    TenantId = tenant, TransportTripId = trip.Id, StudentTransportId = x.Assignment,
                    StudentId = x.StudentId, StudentEnrollmentId = x.StudentEnrollmentId,
                    StudentCodeSnapshot = x.StudentCode, PickupStopId = x.PickupStopId!.Value,
                    DropStopId = x.DropStopId!.Value, PickupStopSnapshot = x.Pickup!,
                    DropStopSnapshot = x.Drop!, CreatedBy = _user.UserId
                }));
                await _db.SaveChangesAsync(token);
                var result = await Query().FirstAsync(x => x.Reference == reference, token);
                return ApiResponse<TransportTripRowDto>.SuccessResponse(result, "Dispatch and rider snapshot recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Trip changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport dispatch uniqueness conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Concurrent dispatch conflict.", 409);
        }
        catch (Exception ex) when (IsRetryableTripSqlConflict(ex))
        {
            _logger.LogWarning(ex, "Transport trip SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Concurrent trip operation; reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport dispatch failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportTripRowDto>.ErrorResponse("Dispatch failed.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<TransportTripRowDto>>> GetAsync(
        DateOnly date, long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<TransportTripRowDto>>.ErrorResponse("Transport permission required.", 403);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (date < today.AddDays(-30) || date > today.AddDays(30) ||
            campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportTripRowDto>>.ErrorResponse("Invalid date or paging.");
        var query = Query().Where(x => x.OperatingDate == date);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber)
            .ThenBy(x => x.Direction).ThenBy(x => x.Reference)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportTripRowDto>>.SuccessResponse(
            new PagedResult<TransportTripRowDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<PagedResult<TransportTripRiderDto>>> GetRidersAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<TransportTripRiderDto>>.ErrorResponse("Transport permission required.", 403);
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportTripRiderDto>>.ErrorResponse("Invalid trip or paging.");
        var tenant = _user.TenantId;
        var trip = await _db.TransportTrips.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference && (x.State == "Dispatched" || x.State == "Completed"))
            .Select(x => new { x.Id, x.CampusId, x.RiderCount }).FirstOrDefaultAsync(ct);
        if (trip == null) return ApiResponse<PagedResult<TransportTripRiderDto>>.ErrorResponse("Dispatched trip not found.", 404);
        if (!await HasCampusAsync(trip.CampusId, ct))
            return ApiResponse<PagedResult<TransportTripRiderDto>>.ErrorResponse("Campus access required.", 403);
        var query = _db.TransportTripRiders.AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.TransportTripId == trip.Id);
        var count = await query.CountAsync(ct);
        if (count != trip.RiderCount)
            return ApiResponse<PagedResult<TransportTripRiderDto>>.ErrorResponse("Frozen manifest integrity conflict.", 409);
        var rows = await query.OrderBy(x => x.StudentCodeSnapshot).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TransportTripRiderDto
            {
                StudentCode = x.StudentCodeSnapshot, PickupStop = x.PickupStopSnapshot,
                DropStop = x.DropStopSnapshot,
                AttendanceState = _db.TransportTripAttendanceEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && !e.IsDeleted && e.TransportTripRiderId == x.Id)
                    .OrderByDescending(e => e.Sequence).Select(e => e.EventType).FirstOrDefault() ?? "Awaiting"
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportTripRiderDto>>.SuccessResponse(
            new PagedResult<TransportTripRiderDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<string>> ExportRidersCsvAsync(Guid reference, CancellationToken ct = default)
    {
        var first = await GetRidersAsync(reference, 1, 100, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 500)
            return ApiResponse<string>.ErrorResponse("More than 500 riders; narrow the trip.", 413);
        var rows = new List<TransportTripRiderDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetRidersAsync(reference, page, 100, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Manifest changed during export.", 409);
            rows.AddRange(next.Data.Items);
        }
        if (rows.Count != first.Data.TotalCount ||
            rows.Select(x => x.StudentCode).Distinct().Count() != rows.Count)
            return ApiResponse<string>.ErrorResponse("Manifest integrity conflict.", 409);
        var csv = new StringBuilder("\uFEFFStudent code,Pickup stop,Drop stop,Attendance\r\n");
        foreach (var row in rows)
            csv.Append(Escape(row.StudentCode)).Append(',').Append(Escape(row.PickupStop))
                .Append(',').Append(Escape(row.DropStop)).Append(',').Append(Escape(row.AttendanceState)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string Escape(string? value)
    {
        var text = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
