using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Historical monthly assignment starts and closures, not physical check-ins/check-outs.
    /// Uses canonical StudentTransport only; groups in SQL without materializing student records.</summary>
    public async Task<ApiResponse<TransportRiderTurnoverDto>> GetRiderTurnoverAsync(
        long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<TransportRiderTurnoverDto>();
        var currentMonth = new DateOnly(_clock.GetLocalNow().Year, _clock.GetLocalNow().Month, 1);
        var end = toMonth ?? currentMonth;
        if (!fromMonth.HasValue && end < new DateOnly(1, 6, 1))
            return ApiResponse<TransportRiderTurnoverDto>.ErrorResponse("Invalid month range.", 400);
        var start = fromMonth ?? end.AddMonths(-5);
        var span = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (campusId is <= 0 || start.Day != 1 || end.Day != 1 || start > end ||
            end > currentMonth || span >= 12)
            return ApiResponse<TransportRiderTurnoverDto>.ErrorResponse("Choose one to twelve completed/current calendar months.", 400);

        var tenant = _currentUser.TenantId;
        var exclusiveEnd = end.AddMonths(1);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on assignment.StudentEnrollmentId equals enrollment.Id
            join route in _routes.GetQueryable().AsNoTracking() on assignment.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on assignment.VehicleId equals vehicle.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on enrollment.CampusId equals campus.Id
            where assignment.TenantId == tenant && enrollment.TenantId == tenant &&
                route.TenantId == tenant && vehicle.TenantId == tenant && campus.TenantId == tenant &&
                !assignment.IsDeleted && !enrollment.IsDeleted && !route.IsDeleted &&
                !vehicle.IsDeleted && campus.IsActive && !campus.IsDeleted &&
                enrollment.StudentId == assignment.StudentId &&
                (!route.CampusId.HasValue || route.CampusId.Value == enrollment.CampusId) &&
                (!vehicle.CampusId.HasValue || vehicle.CampusId.Value == enrollment.CampusId) &&
                (!campusId.HasValue || enrollment.CampusId == campusId.Value)
            select new { assignment.StartDate, assignment.EndDate, assignment.State, enrollment.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var starts = await query.Where(x => x.State != TransportAssignmentState.Cancelled &&
                x.StartDate >= start && x.StartDate < exclusiveEnd && x.StartDate <= today)
            .GroupBy(x => new { x.StartDate.Year, x.StartDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var closures = await query.Where(x => x.State == TransportAssignmentState.Closed &&
                x.EndDate.HasValue && x.EndDate.Value >= start &&
                x.EndDate.Value < exclusiveEnd && x.EndDate.Value <= today)
            .GroupBy(x => new { Year = x.EndDate!.Value.Year, Month = x.EndDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var startCounts = starts.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        var closeCounts = closures.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        var months = new List<TransportRiderTurnoverMonthDto>();
        for (var month = start; month <= end; month = month.AddMonths(1))
        {
            startCounts.TryGetValue((month.Year, month.Month), out var started);
            closeCounts.TryGetValue((month.Year, month.Month), out var closed);
            months.Add(new TransportRiderTurnoverMonthDto
            {
                Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                AssignmentStarts = SuppressRiderTurnoverSmallCohort(started),
                AssignmentClosures = SuppressRiderTurnoverSmallCohort(closed)
            });
        }
        return ApiResponse<TransportRiderTurnoverDto>.SuccessResponse(new TransportRiderTurnoverDto
        {
            FromMonth = start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ToMonth = end.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Months = months
        });
    }

    public async Task<ApiResponse<string>> ExportRiderTurnoverCsvAsync(
        long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default)
    {
        var report = await GetRiderTurnoverAsync(campusId, fromMonth, toMonth, cancellationToken);
        if (!report.Success || report.Data == null)
            return ApiResponse<string>.ErrorResponse(report.Message ?? "Rider turnover unavailable.", report.StatusCode);
        var csv = new StringBuilder("Month,Assignment Starts,Assignment Closures\r\n");
        foreach (var row in report.Data.Months)
            csv.Append(row.Month).Append(',')
                .Append(row.AssignmentStarts?.ToString(CultureInfo.InvariantCulture) ?? "Suppressed")
                .Append(',')
                .Append(row.AssignmentClosures?.ToString(CultureInfo.InvariantCulture) ?? "Suppressed")
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static int? SuppressRiderTurnoverSmallCohort(int count) => count is > 0 and < 5 ? null : count;
}
