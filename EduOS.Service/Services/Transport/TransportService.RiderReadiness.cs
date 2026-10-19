using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Operational exceptions for current/upcoming rider assignments. Opaque references only;
    /// student identity, contact details and monetary data never enter the projection.
    /// </summary>
    public async Task<ApiResponse<PagedResult<TransportRiderReadinessRowDto>>> GetRiderReadinessAsync(
        long? campusId, int page, int pageSize, string? search, bool attentionOnly,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportRiderReadinessRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportRiderReadinessRowDto>>.ErrorResponse("Invalid rider readiness filters.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on assignment.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on assignment.StudentId equals student.Id
            join route in _routes.GetQueryable().AsNoTracking() on assignment.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on assignment.VehicleId equals vehicle.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on enrollment.CampusId equals campus.Id
            where assignment.TenantId == tenant && !assignment.IsDeleted &&
                enrollment.TenantId == tenant && student.TenantId == tenant &&
                route.TenantId == tenant && vehicle.TenantId == tenant &&
                campus.TenantId == tenant && !campus.IsDeleted && campus.IsActive &&
                assignment.State == TransportAssignmentState.Active &&
                (!assignment.EndDate.HasValue || assignment.EndDate.Value >= today) &&
                (!campusId.HasValue || campus.Id == campusId.Value)
            select new
            {
                assignment.PublicId, assignment.StartDate, assignment.EndDate,
                CampusId = campus.Id, CampusName = campus.Name,
                route.Name, vehicle.VehicleNumber,
                BadEnrollment = enrollment.IsDeleted || student.IsDeleted ||
                    enrollment.StudentId != assignment.StudentId || !enrollment.IsCurrent ||
                    enrollment.State != EnrollmentState.Active || student.StatusCode != "Active",
                BadRoute = route.IsDeleted || !route.IsActive ||
                    (route.CampusId.HasValue && route.CampusId.Value != campus.Id),
                BadVehicle = vehicle.IsDeleted || !vehicle.IsActive || vehicle.Capacity <= 0 ||
                    (vehicle.CampusId.HasValue && vehicle.CampusId.Value != campus.Id),
                BadPickup = assignment.PickupStopId.HasValue &&
                    !_stops.GetQueryable().Any(stop => stop.TenantId == tenant && !stop.IsDeleted &&
                        stop.Id == assignment.PickupStopId.Value && stop.RouteId == assignment.RouteId && stop.IsActive),
                BadDrop = assignment.DropStopId.HasValue &&
                    !_stops.GetQueryable().Any(stop => stop.TenantId == tenant && !stop.IsDeleted &&
                        stop.Id == assignment.DropStopId.Value && stop.RouteId == assignment.RouteId && stop.IsActive),
                MissingLink = !_vehicleRoutes.GetQueryable().Any(link =>
                    link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                    link.RouteId == assignment.RouteId && link.VehicleId == assignment.VehicleId &&
                    link.EffectiveFrom <= (assignment.StartDate > today ? assignment.StartDate : today) &&
                    (!link.EffectiveTo.HasValue ||
                        link.EffectiveTo.Value >= (assignment.StartDate > today ? assignment.StartDate : today)))
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.CampusName.Contains(term) ||
                x.Name.Contains(term) || x.VehicleNumber.Contains(term));
        if (attentionOnly)
            query = query.Where(x => x.BadEnrollment || x.BadRoute || x.BadVehicle ||
                x.BadPickup || x.BadDrop || x.MissingLink);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.CampusName).ThenBy(x => x.Name)
            .ThenBy(x => x.VehicleNumber).ThenBy(x => x.PublicId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<TransportRiderReadinessRowDto>>.SuccessResponse(
            new PagedResult<TransportRiderReadinessRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x =>
                {
                    var reason = x.BadEnrollment ? "Enrollment needs review" :
                        x.BadRoute ? "Route inactive or campus mismatch" :
                        x.BadVehicle ? "Vehicle inactive, invalid capacity or campus mismatch" :
                        x.BadPickup ? "Pickup stop invalid" :
                        x.BadDrop ? "Drop stop invalid" :
                        x.MissingLink ? "No effective route-vehicle assignment" : "Ready";
                    return new TransportRiderReadinessRowDto
                    {
                        AssignmentReference = x.PublicId, CampusId = x.CampusId,
                        CampusName = x.CampusName, RouteName = x.Name,
                        VehicleNumber = x.VehicleNumber, StartDate = x.StartDate,
                        EndDate = x.EndDate, Readiness = reason, NeedsAttention = reason != "Ready"
                    };
                }).ToList()
            });
    }

    public async Task<ApiResponse<string>> ExportRiderReadinessCsvAsync(
        long? campusId, string? search, bool attentionOnly, CancellationToken cancellationToken = default)
    {
        var first = await GetRiderReadinessAsync(campusId, 1, 100, search, attentionOnly, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Rider readiness unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 assignments. Narrow campus or search.", 413);
        var rows = new List<TransportRiderReadinessRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetRiderReadinessAsync(campusId, page, 100, search, attentionOnly, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Readiness changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.AssignmentReference)))
            return ApiResponse<string>.ErrorResponse("Readiness changed during export. Retry.", 409);
        var csv = new StringBuilder("Assignment Reference,Campus,Route,Vehicle,Start Date,End Date,Readiness,Needs Attention\r\n");
        foreach (var row in rows)
            csv.Append(row.AssignmentReference).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.RouteName)).Append(',')
                .Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(CsvCell(row.Readiness)).Append(',')
                .Append(row.NeedsAttention ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
