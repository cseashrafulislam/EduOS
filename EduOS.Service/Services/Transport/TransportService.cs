using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Service.Services.Transport;

public sealed class TransportService : ITransportService
{
    private readonly IGenericRepository<TransportRoute> _routes;
    private readonly IGenericRepository<Vehicle> _vehicles;
    private readonly IGenericRepository<StudentTransport> _assignments;
    private readonly IGenericRepository<Student> _students;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<TransportService> _logger;

    public TransportService(IGenericRepository<TransportRoute> routes, IGenericRepository<Vehicle> vehicles, IGenericRepository<StudentTransport> assignments, IGenericRepository<Student> students, IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<TransportService> logger)
    {
        _routes = routes; _vehicles = vehicles; _assignments = assignments; _students = students; _unitOfWork = unitOfWork; _currentUser = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<TransportRouteDto>>> GetRoutesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<TransportRouteDto>>();
        var tenantId = _currentUser.TenantId;
        IReadOnlyList<TransportRouteDto> rows = await _routes.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Name).Select(x => new TransportRouteDto { Reference = x.PublicId, Name = x.Name, Distance = x.Distance, Fare = x.Fare, Description = x.Description }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<TransportRouteDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<TransportVehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<IReadOnlyList<TransportVehicleDto>>();
        var tenantId = _currentUser.TenantId;
        IReadOnlyList<TransportVehicleDto> rows = await _vehicles.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.VehicleNo).Select(x => new TransportVehicleDto { Reference = x.PublicId, VehicleNo = x.VehicleNo, Type = x.Type, Capacity = x.Capacity, ActiveAssignments = _assignments.GetQueryable().Count(a => a.TenantId == tenantId && a.VehicleId == x.Id && a.IsActive), DriverName = x.DriverName, DriverPhone = x.DriverPhone, RouteReference = x.Route != null ? x.Route.PublicId : null, RouteName = x.Route != null ? x.Route.Name : null }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<TransportVehicleDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<StudentTransportDto?>> GetMyAssignmentAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<StudentTransportDto?>();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var studentIds = await _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var assignment = await QueryAssignments().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && studentIds.Contains(x.StudentId)).OrderByDescending(x => x.StartDate).FirstOrDefaultAsync(cancellationToken);
        return ApiResponse<StudentTransportDto?>.SuccessResponse(assignment == null ? null : Map(assignment));
    }

    public async Task<ApiResponse<StudentTransportDto>> AssignAsync(AssignTransportDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty || request.VehicleReference == Guid.Empty || request.RouteReference == Guid.Empty) return Error("Student, vehicle, route and client request references are required.");
        if (request.MonthlyFare.HasValue && request.MonthlyFare.Value < 0) return Error("Monthly fare cannot be negative.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var existing = await QueryAssignments().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (existing != null) return ApiResponse<StudentTransportDto>.SuccessResponse(Map(existing), "Transport assignment was already processed.");
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.StudentReference && x.IsActive, cancellationToken);
            if (student == null) return Error("Student not found.", 404);
            var vehicle = await _vehicles.GetQueryable().AsNoTracking().Include(x => x.Route).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.VehicleReference && x.IsActive, cancellationToken);
            if (vehicle == null) return Error("Vehicle not found.", 404);
            if (vehicle.Capacity <= 0) return Error("Vehicle capacity is not configured.", 409);
            var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.RouteReference && x.IsActive, cancellationToken);
            if (route == null) return Error("Route not found.", 404);
            if (vehicle.RouteId.HasValue && vehicle.RouteId.Value != route.Id) return Error("Vehicle is assigned to a different route.", 409);
            if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.StudentId == student.Id && x.IsActive, cancellationToken)) return Error("Student already has an active transport assignment.", 409);
            var activeCount = await _assignments.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.VehicleId == vehicle.Id && x.IsActive, cancellationToken);
            if (activeCount >= vehicle.Capacity) return Error("Vehicle has reached its capacity.", 409);
            var today = _clock.GetLocalNow().Date;
            var startDate = request.StartDate == default ? today : request.StartDate.Date;
            var now = _clock.GetUtcNow().UtcDateTime;
            var assignment = new StudentTransport { TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId, StudentId = student.Id, VehicleId = vehicle.Id, RouteId = route.Id, PickupPoint = string.IsNullOrWhiteSpace(request.PickupPoint) ? null : request.PickupPoint.Trim(), StartDate = startDate, MonthlyFare = request.MonthlyFare ?? route.Fare, IsActive = true, CreatedAt = now, CreatedBy = _currentUser.UserId };
            await _assignments.AddAsync(assignment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            assignment.Student = student; assignment.Vehicle = vehicle; assignment.Route = route;
            return new ApiResponse<StudentTransportDto> { Success = true, StatusCode = 201, Message = "Transport assigned.", Data = Map(assignment) };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting transport assignment for tenant {TenantId}", tenantId);
            return Error("Transport assignment conflicts with another update. Reload and try again.", 409);
        }
    }

    public async Task<ApiResponse<StudentTransportDto>> CloseAsync(Guid reference, CloseTransportDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null) return Error("Close request is invalid.");
        var tenantId = _currentUser.TenantId;
        try
        {
            var assignment = await QueryAssignments().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == reference, cancellationToken);
            if (assignment == null) return Error("Transport assignment not found.", 404);
            if (!assignment.IsActive) return ApiResponse<StudentTransportDto>.SuccessResponse(Map(assignment), "Transport assignment is already closed.");
            var endDate = request.EndDate?.Date ?? _clock.GetLocalNow().Date;
            if (endDate < assignment.StartDate.Date) return Error("End date cannot be before start date.");
            assignment.EndDate = endDate; assignment.IsActive = false; assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime; assignment.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<StudentTransportDto>.SuccessResponse(Map(assignment), "Transport assignment closed.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent transport close {Reference} for tenant {TenantId}", reference, tenantId);
            return Error("The assignment changed by another user. Reload and try again.", 409);
        }
    }

    private IQueryable<StudentTransport> QueryAssignments() => _assignments.GetQueryable().Include(x => x.Student).Include(x => x.Vehicle).Include(x => x.Route);
    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0;
    private bool CanManage() => CanRead() && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("TransportManager"));
    private static StudentTransportDto Map(StudentTransport x) => new() { Reference = x.PublicId, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentName = x.Student?.FullName ?? string.Empty, VehicleReference = x.Vehicle?.PublicId ?? Guid.Empty, VehicleNo = x.Vehicle?.VehicleNo ?? string.Empty, RouteReference = x.Route?.PublicId ?? Guid.Empty, RouteName = x.Route?.Name ?? string.Empty, PickupPoint = x.PickupPoint, StartDate = x.StartDate, EndDate = x.EndDate, MonthlyFare = x.MonthlyFare, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Transport access is required.", 403);
    private static ApiResponse<StudentTransportDto> Error(string message, int status = 400) => ApiResponse<StudentTransportDto>.ErrorResponse(message, status);
}
