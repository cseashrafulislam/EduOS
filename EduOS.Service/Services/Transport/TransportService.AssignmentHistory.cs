using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Campus-authorized, paged historical read over canonical StudentTransport records.</summary>
    public async Task<ApiResponse<PagedResult<TransportAssignmentHistoryRowDto>>> GetAssignmentHistoryAsync(
        long? campusId, int page, int pageSize, string? search, TransportAssignmentState? state,
        DateOnly? fromDate, DateOnly? toDate, Guid? routeReference, Guid? vehicleReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportAssignmentHistoryRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100 ||
            (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value) ||
            (routeReference.HasValue && routeReference.Value == Guid.Empty) ||
            (vehicleReference.HasValue && vehicleReference.Value == Guid.Empty) ||
            (state.HasValue && state.Value != TransportAssignmentState.Active &&
                state.Value != TransportAssignmentState.Closed &&
                state.Value != TransportAssignmentState.Cancelled))
            return ApiResponse<PagedResult<TransportAssignmentHistoryRowDto>>.ErrorResponse("Invalid transport assignment history filter.");

        var tenant = _currentUser.TenantId;
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on assignment.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on assignment.StudentId equals student.Id
            join route in _routes.GetQueryable().AsNoTracking() on assignment.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on assignment.VehicleId equals vehicle.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on enrollment.CampusId equals campus.Id
            where assignment.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && route.TenantId == tenant &&
                vehicle.TenantId == tenant && campus.TenantId == tenant &&
                enrollment.StudentId == assignment.StudentId &&
                (!route.CampusId.HasValue || route.CampusId.Value == enrollment.CampusId) &&
                (!vehicle.CampusId.HasValue || vehicle.CampusId.Value == enrollment.CampusId) &&
                (!campusId.HasValue || enrollment.CampusId == campusId.Value)
            select new
            {
                assignment.Id, assignment.PublicId, assignment.RowVersion, assignment.StartDate, assignment.EndDate,
                assignment.State, CampusId = campus.Id, CampusName = campus.Name,
                StudentName = student.FullName, student.StudentCode,
                RouteName = route.Name, RouteReference = route.PublicId,
                vehicle.VehicleNumber, VehicleReference = vehicle.PublicId
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.StudentName.Contains(term) || x.StudentCode.Contains(term) ||
                x.RouteName.Contains(term) || x.VehicleNumber.Contains(term));
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        if (fromDate.HasValue) query = query.Where(x => x.StartDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.StartDate <= toDate.Value);
        if (routeReference.HasValue) query = query.Where(x => x.RouteReference == routeReference.Value);
        if (vehicleReference.HasValue) query = query.Where(x => x.VehicleReference == vehicleReference.Value);

        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<TransportAssignmentHistoryRowDto>>.SuccessResponse(
            new PagedResult<TransportAssignmentHistoryRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = records.Select(x => new TransportAssignmentHistoryRowDto
                {
                    Reference = x.PublicId, RowVersion = Convert.ToBase64String(x.RowVersion), CampusId = x.CampusId, CampusName = x.CampusName,
                    StudentName = x.StudentName, StudentCode = x.StudentCode,
                    RouteName = x.RouteName, VehicleNumber = x.VehicleNumber,
                    StartDate = x.StartDate, EndDate = x.EndDate, State = x.State
                }).ToList()
            });
    }
}
