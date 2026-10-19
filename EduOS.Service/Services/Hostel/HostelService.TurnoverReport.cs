using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Historical monthly allocation starts and closures, not physical check-ins/check-outs.
    /// Uses canonical allocations only; performs grouped SQL counts without materializing student records.</summary>
    public async Task<ApiResponse<HostelTurnoverReportDto>> GetTurnoverReportAsync(
        long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<HostelTurnoverReportDto>();
        var currentMonth = new DateOnly(_clock.GetLocalNow().Year, _clock.GetLocalNow().Month, 1);
        var end = toMonth ?? currentMonth;
        if (!fromMonth.HasValue && end < new DateOnly(1, 6, 1))
            return ApiResponse<HostelTurnoverReportDto>.ErrorResponse("Invalid month range.", 400);
        var start = fromMonth ?? end.AddMonths(-5);
        var span = (end.Year - start.Year) * 12 + end.Month - start.Month;
        if (campusId is <= 0 || start.Day != 1 || end.Day != 1 || start > end ||
            end > currentMonth || span >= 12)
            return ApiResponse<HostelTurnoverReportDto>.ErrorResponse("Choose one to twelve completed/current calendar months.", 400);

        var tenant = _currentUser.TenantId;
        var exclusiveEnd = end.AddMonths(1);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on allocation.StudentEnrollmentId equals enrollment.Id
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where allocation.TenantId == tenant && enrollment.TenantId == tenant &&
                bed.TenantId == tenant && room.TenantId == tenant &&
                hostel.TenantId == tenant && campus.TenantId == tenant &&
                !allocation.IsDeleted && !enrollment.IsDeleted && !bed.IsDeleted &&
                !room.IsDeleted && !hostel.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                enrollment.StudentId == allocation.StudentId && enrollment.CampusId == hostel.CampusId &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new { allocation.StartDate, allocation.EndDate, allocation.State, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var starts = await query.Where(x => x.State != HostelAllocationState.Cancelled &&
                x.StartDate >= start && x.StartDate < exclusiveEnd && x.StartDate <= today)
            .GroupBy(x => new { x.StartDate.Year, x.StartDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var closures = await query.Where(x => x.State == HostelAllocationState.Closed &&
                x.EndDate.HasValue && x.EndDate.Value >= start &&
                x.EndDate.Value < exclusiveEnd && x.EndDate.Value <= today)
            .GroupBy(x => new { Year = x.EndDate!.Value.Year, Month = x.EndDate!.Value.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var startCounts = starts.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        var closeCounts = closures.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        var months = new List<HostelTurnoverMonthDto>();
        for (var month = start; month <= end; month = month.AddMonths(1))
        {
            startCounts.TryGetValue((month.Year, month.Month), out var started);
            closeCounts.TryGetValue((month.Year, month.Month), out var closed);
            months.Add(new HostelTurnoverMonthDto
            {
                Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                AllocationStarts = SuppressSmallCohort(started),
                AllocationClosures = SuppressSmallCohort(closed)
            });
        }
        return ApiResponse<HostelTurnoverReportDto>.SuccessResponse(new HostelTurnoverReportDto
        {
            FromMonth = start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ToMonth = end.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime,
            Months = months
        });
    }

    public async Task<ApiResponse<string>> ExportTurnoverReportCsvAsync(
        long? campusId, DateOnly? fromMonth, DateOnly? toMonth, CancellationToken cancellationToken = default)
    {
        var report = await GetTurnoverReportAsync(campusId, fromMonth, toMonth, cancellationToken);
        if (!report.Success || report.Data == null)
            return ApiResponse<string>.ErrorResponse(report.Message ?? "Turnover report unavailable.", report.StatusCode);
        var csv = new StringBuilder("Month,Allocation Starts,Allocation Closures\r\n");
        foreach (var row in report.Data.Months)
            csv.Append(row.Month).Append(',')
                .Append(row.AllocationStarts?.ToString(CultureInfo.InvariantCulture) ?? "Suppressed")
                .Append(',')
                .Append(row.AllocationClosures?.ToString(CultureInfo.InvariantCulture) ?? "Suppressed")
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static int? SuppressSmallCohort(int count) => count is > 0 and < 5 ? null : count;
}
