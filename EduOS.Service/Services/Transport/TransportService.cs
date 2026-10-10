using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Service.Services.Transport;

public sealed class TransportService : ITransportService
{
    private readonly IGenericRepository<TransportRoute> _routes;
    private readonly IGenericRepository<RouteStop> _stops;
    private readonly IGenericRepository<Vehicle> _vehicles;
    private readonly IGenericRepository<RouteVehicleAssignment> _vehicleRoutes;
    private readonly IGenericRepository<StudentTransport> _assignments;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<TransportService> _logger;

    public TransportService(IGenericRepository<TransportRoute> routes, IGenericRepository<RouteStop> stops,
        IGenericRepository<Vehicle> vehicles, IGenericRepository<RouteVehicleAssignment> vehicleRoutes,
        IGenericRepository<StudentTransport> assignments, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Student> students, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        TimeProvider clock, ILogger<TransportService> logger)
    {
        _routes = routes;
        _stops = stops;
        _vehicles = vehicles;
        _vehicleRoutes = vehicleRoutes;
        _assignments = assignments;
        _enrollments = enrollments;
        _students = students;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<RouteDto>>> GetRoutesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<RouteDto>>();
        var tenant = _currentUser.TenantId;
        var routes = await _routes.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var ids = routes.Select(x => x.Id).ToArray();
        var stops = await _stops.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && ids.Contains(x.RouteId))
            .OrderBy(x => x.SequenceNo).ToListAsync(cancellationToken);
        var byRoute = stops.GroupBy(x => x.RouteId).ToDictionary(g => g.Key, g => g.Select(x => new RouteStopDto
        {
            Id = x.Id, Name = x.Name, SequenceNo = x.SequenceNo,
            FareFromOrigin = x.FareFromOrigin, IsActive = x.IsActive,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToArray());
        IReadOnlyList<RouteDto> result = routes.Select(x => new RouteDto
        {
            Id = x.Id, Reference = x.PublicId, Name = x.Name, Code = x.Code,
            DefaultFare = x.DefaultFare, IsActive = x.IsActive,
            Stops = byRoute.TryGetValue(x.Id, out var list) ? list : Array.Empty<RouteStopDto>(),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<IReadOnlyList<RouteDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<VehicleDto>>();
        var tenant = _currentUser.TenantId;
        var vehicles = await _vehicles.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive)
            .OrderBy(x => x.VehicleNumber).ToListAsync(cancellationToken);
        var ids = vehicles.Select(x => x.Id).ToArray();
        var counts = await _assignments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.State == TransportAssignmentState.Active && ids.Contains(x.VehicleId))
            .GroupBy(x => x.VehicleId).Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VehicleId, x => x.Count, cancellationToken);
        IReadOnlyList<VehicleDto> result = vehicles.Select(x => new VehicleDto
        {
            Id = x.Id, Reference = x.PublicId, VehicleNumber = x.VehicleNumber,
            VehicleType = x.VehicleType, Capacity = x.Capacity, IsActive = x.IsActive,
            ActiveAssignments = counts.GetValueOrDefault(x.Id),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<IReadOnlyList<VehicleDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<PagedResult<TransportStudentOptionDto>>> GetEligibleStudentsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportStudentOptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<TransportStudentOptionDto>>.ErrorResponse("Invalid paging parameters.");
        var tenant = _currentUser.TenantId;
        var query = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant && student.StatusCode == "Active" &&
                          enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                          !_assignments.GetQueryable().Any(x => x.TenantId == tenant && x.StudentId == student.Id &&
                              x.State == TransportAssignmentState.Active)
                    select new { enrollment.PublicId, student.FullName, student.StudentCode, enrollment.RollNo };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<TransportStudentOptionDto>>.ErrorResponse("Search is too long.");
            query = query.Where(x => x.FullName.Contains(term) || x.StudentCode.Contains(term) || x.RollNo.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.StudentCode)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TransportStudentOptionDto
            {
                EnrollmentReference = x.PublicId, StudentName = x.FullName,
                StudentCode = x.StudentCode, Roll = x.RollNo
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<TransportStudentOptionDto>>.SuccessResponse(new PagedResult<TransportStudentOptionDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<TransportAssignmentRowDto>>> GetActiveAssignmentsAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportAssignmentRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return ApiResponse<PagedResult<TransportAssignmentRowDto>>.ErrorResponse("Invalid paging parameters.");
        var tenant = _currentUser.TenantId;
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on assignment.StudentId equals student.Id
                    join route in _routes.GetQueryable().AsNoTracking() on assignment.RouteId equals route.Id
                    join vehicle in _vehicles.GetQueryable().AsNoTracking() on assignment.VehicleId equals vehicle.Id
                    where assignment.TenantId == tenant && student.TenantId == tenant &&
                          route.TenantId == tenant && vehicle.TenantId == tenant &&
                          assignment.State == TransportAssignmentState.Active
                    select new { assignment.PublicId, assignment.StartDate, assignment.RowVersion,
                        student.FullName, student.StudentCode, RouteName = route.Name, vehicle.VehicleNumber };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<TransportAssignmentRowDto>>.ErrorResponse("Search is too long.");
            query = query.Where(x => x.FullName.Contains(term) || x.StudentCode.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderBy(x => x.FullName).ThenBy(x => x.StudentCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = records.Select(x => new TransportAssignmentRowDto
        {
            Reference = x.PublicId, StartDate = x.StartDate, RowVersion = Convert.ToBase64String(x.RowVersion),
            StudentName = x.FullName, RouteName = x.RouteName, VehicleNumber = x.VehicleNumber
        }).ToList();
        return ApiResponse<PagedResult<TransportAssignmentRowDto>>.SuccessResponse(new PagedResult<TransportAssignmentRowDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<StudentTransportDto?>> GetMyAssignmentAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<StudentTransportDto?>();
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var studentIds = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.UserId == _currentUser.UserId && x.StatusCode == "Active").Select(x => x.Id);
        var assignment = await _assignments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.State == TransportAssignmentState.Active && studentIds.Contains(x.StudentId) &&
            _enrollments.GetQueryable().Any(e => e.TenantId == tenant && e.Id == x.StudentEnrollmentId &&
                e.StudentId == x.StudentId && e.IsCurrent && e.State == EnrollmentState.Active) &&
            x.StartDate <= today && (!x.EndDate.HasValue || x.EndDate >= today))
            .OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(cancellationToken);
        var result = assignment == null ? null : await MapAsync(assignment, cancellationToken);
        return ApiResponse<StudentTransportDto?>.SuccessResponse(result);
    }

    public async Task<ApiResponse<StudentTransportDto>> AssignAsync(AssignStudentTransportRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentEnrollmentReference == Guid.Empty ||
            request.VehicleReference == Guid.Empty || request.RouteReference == Guid.Empty)
            return Error("Enrollment, route, vehicle and client request references are required.");
        if (request.MonthlyFare.HasValue && request.MonthlyFare.Value < 0) return Error("Monthly fare cannot be negative.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var startDate = request.StartDate == default ? today : request.StartDate;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var old = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (old != null)
            {
                if (old.RouteId <= 0 || old.VehicleId <= 0)
                    return Error("Existing request is inconsistent.", 409);
                var replay = await MapAsync(old, cancellationToken);
                if (replay.StudentEnrollmentReference != request.StudentEnrollmentReference ||
                    replay.RouteReference != request.RouteReference ||
                    replay.VehicleReference != request.VehicleReference ||
                    replay.PickupStopId != request.PickupStopId || replay.DropStopId != request.DropStopId ||
                    (request.StartDate != default && replay.StartDate != request.StartDate) ||
                    (request.MonthlyFare.HasValue && replay.MonthlyFare != request.MonthlyFare.Value))
                    return Error("Client request ID was reused for different transport assignment data.", 409);
                scope.Complete();
                return ApiResponse<StudentTransportDto>.SuccessResponse(replay, "Transport assignment already processed.");
            }
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.StudentEnrollmentReference && x.IsCurrent &&
                x.State == EnrollmentState.Active, cancellationToken);
            if (enrollment == null) return Error("Active student enrollment not found.", 404);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == enrollment.StudentId && x.StatusCode == "Active", cancellationToken);
            if (student == null) return Error("Student not found.", 404);
            var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.RouteReference && x.IsActive, cancellationToken);
            var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.VehicleReference && x.IsActive, cancellationToken);
            if (route == null || vehicle == null || vehicle.Capacity <= 0)
                return Error("Route or vehicle is unavailable.", 404);
            if ((route.CampusId.HasValue && route.CampusId != enrollment.CampusId) ||
                (vehicle.CampusId.HasValue && vehicle.CampusId != enrollment.CampusId))
                return Error("Route or vehicle is outside the student's campus.", 409);
            var vehicleRoute = await _vehicleRoutes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.RouteId == route.Id && x.VehicleId == vehicle.Id && x.IsCurrent &&
                x.EffectiveFrom <= startDate && (!x.EffectiveTo.HasValue || x.EffectiveTo >= startDate), cancellationToken);
            if (vehicleRoute == null) return Error("Vehicle must have an active assignment to the selected route.", 409);
            var stopIds = new[] { request.PickupStopId, request.DropStopId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            if (stopIds.Length > 0)
            {
                var foundStops = await _stops.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                    x.RouteId == route.Id && x.IsActive && stopIds.Contains(x.Id), cancellationToken);
                if (foundStops != stopIds.Length) return Error("One or more stops do not belong to the selected route.", 409);
            }
            if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.StudentId == student.Id && x.State == TransportAssignmentState.Active, cancellationToken))
                return Error("Student already has an active transport assignment.", 409);
            var occupied = await _assignments.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                x.VehicleId == vehicle.Id && x.State == TransportAssignmentState.Active, cancellationToken);
            if (occupied >= vehicle.Capacity) return Error("Vehicle has reached capacity.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new StudentTransport
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                StudentId = student.Id, StudentEnrollmentId = enrollment.Id,
                RouteId = route.Id, VehicleId = vehicle.Id, RouteVehicleAssignmentId = vehicleRoute.Id,
                PickupStopId = request.PickupStopId, DropStopId = request.DropStopId,
                StartDate = startDate, MonthlyFare = request.MonthlyFare ?? route.DefaultFare,
                State = TransportAssignmentState.Active, CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _assignments.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var response = await MapAsync(entity, cancellationToken);
            scope.Complete();
            return new ApiResponse<StudentTransportDto>
            {
                Success = true, StatusCode = 201, Message = "Transport assigned.",
                Data = response
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport assignment conflict for tenant {TenantId}", tenant);
            return Error("Transport assignment conflicts with another update.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized transport transaction aborted for tenant {TenantId}", tenant);
            return Error("Transport assignment conflicts with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transport assignment failed for tenant {TenantId}", tenant);
            return Error("Transport assignment could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<StudentTransportDto>> CloseAsync(Guid reference, CloseStudentTransportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null) return Error("Transport close request is invalid.");
        var tenant = _currentUser.TenantId;
        try
        {
            var entity = await _assignments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == reference, cancellationToken);
            if (entity == null) return Error("Assignment not found.", 404);
            if (entity.State != TransportAssignmentState.Active)
                return ApiResponse<StudentTransportDto>.SuccessResponse(await MapAsync(entity, cancellationToken), "Assignment already closed.");
            if (!TryDecodeVersion(request.RowVersion, out var version) || !entity.RowVersion.AsSpan().SequenceEqual(version))
                return Error("Assignment was changed. Reload and retry.", 409);
            var endDate = request.EndDate == default ? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime) : request.EndDate;
            if (endDate < entity.StartDate) return Error("End date cannot precede start date.");
            entity.EndDate = endDate;
            entity.State = TransportAssignmentState.Closed;
            entity.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            entity.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<StudentTransportDto>.SuccessResponse(await MapAsync(entity, cancellationToken), "Assignment closed.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Assignment was changed by another user.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transport close failed for tenant {TenantId}", tenant);
            return Error("Transport assignment could not be closed.", 500);
        }
    }

    private async Task<StudentTransportDto> MapAsync(StudentTransport value, CancellationToken ct)
    {
        var tenant = _currentUser.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == value.StudentId).Select(x => new { x.PublicId, x.FullName }).FirstOrDefaultAsync(ct);
        var route = await _routes.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == value.RouteId).Select(x => new { x.PublicId, x.Name }).FirstOrDefaultAsync(ct);
        var vehicle = await _vehicles.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == value.VehicleId).Select(x => new { x.PublicId, x.VehicleNumber }).FirstOrDefaultAsync(ct);
        var enrollmentReference = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Id == value.StudentEnrollmentId).Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        var stopIds = new[] { value.PickupStopId, value.DropStopId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var stops = await _stops.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && stopIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return new StudentTransportDto
        {
            Id = value.Id, Reference = value.PublicId,
            StudentEnrollmentReference = enrollmentReference,
            StudentReference = student?.PublicId ?? Guid.Empty, StudentName = student?.FullName ?? string.Empty,
            RouteReference = route?.PublicId ?? Guid.Empty, RouteName = route?.Name ?? string.Empty,
            VehicleReference = vehicle?.PublicId ?? Guid.Empty, VehicleNumber = vehicle?.VehicleNumber ?? string.Empty,
            PickupStopId = value.PickupStopId, DropStopId = value.DropStopId,
            PickupStopName = value.PickupStopId.HasValue ? stops.GetValueOrDefault(value.PickupStopId.Value) : null,
            DropStopName = value.DropStopId.HasValue ? stops.GetValueOrDefault(value.DropStopId.Value) : null,
            StartDate = value.StartDate, EndDate = value.EndDate, MonthlyFare = value.MonthlyFare,
            State = value.State, RowVersion = Convert.ToBase64String(value.RowVersion)
        };
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin ||
        _currentUser.IsInRole("Principal") || _currentUser.IsInRole("TransportManager"));
    private static bool TryDecodeVersion(string? supplied, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { version = Convert.FromBase64String(supplied); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Transport permission is required.", 403);
    private static ApiResponse<StudentTransportDto> Error(string message, int code = 400) =>
        ApiResponse<StudentTransportDto>.ErrorResponse(message, code);
}
