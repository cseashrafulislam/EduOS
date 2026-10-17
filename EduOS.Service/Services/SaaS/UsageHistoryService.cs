using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

/// <summary>Tenant-admin daily operational counts; never represents unmetered SMS, email or storage as measured.</summary>
public sealed class UsageHistoryService(
    EduOSDbContext db, ICurrentUserService user, ILogger<UsageHistoryService> logger) : IUsageHistoryService
{
    private bool CanManage => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;
    private static UsageStatisticsDto Map(UsageStatistics x) => new()
    {
        PeriodStart = x.PeriodStart, PeriodEnd = x.PeriodEnd,
        ActiveStudents = x.ActiveStudents, ActiveEmployees = x.ActiveEmployees,
        ActiveCampuses = x.ActiveCampuses, StorageBytes = x.StorageBytes,
        SmsCount = x.SmsCount, EmailCount = x.EmailCount
    };

    public async Task<ApiResponse<PagedResult<UsageStatisticsDto>>> GetHistoryAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Invalid paging.");
        var query = db.UsageStatistics.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<UsageStatisticsDto>>.SuccessResponse(new()
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<UsageStatisticsDto>> CaptureTodayAsync(CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<UsageStatisticsDto>.ErrorResponse("Tenant administrator required.", 403);
        var tenantId = user.TenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId && !x.IsDeleted, ct);
        if (tenant == null) return ApiResponse<UsageStatisticsDto>.ErrorResponse("Institution not found.", 404);
        var localToday = DateTime.UtcNow;
        try
        {
            if (!string.IsNullOrWhiteSpace(tenant.TimeZoneId))
                localToday = TimeZoneInfo.ConvertTimeFromUtc(localToday, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId));
        }
        catch (TimeZoneNotFoundException) { logger.LogWarning("Unknown tenant timezone {TenantId}; UTC used.", tenantId); }
        catch (InvalidTimeZoneException) { logger.LogWarning("Invalid tenant timezone {TenantId}; UTC used.", tenantId); }
        var day = DateOnly.FromDateTime(localToday);
        var existing = await db.UsageStatistics.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && !x.IsDeleted && x.PeriodStart == day && x.PeriodEnd == day, ct);
        if (existing != null)
            return ApiResponse<UsageStatisticsDto>.SuccessResponse(Map(existing), "Today's snapshot already exists.");
        var students = await db.Students.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && !x.IsDeleted && x.StatusCode == "Active", ct);
        var employees = await db.Employees.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && !x.IsDeleted && x.State == EmployeeState.Active, ct);
        var campuses = await db.Campuses.AsNoTracking().CountAsync(x =>
            x.TenantId == tenantId && !x.IsDeleted && x.IsActive, ct);
        var row = new UsageStatistics
        {
            TenantId = tenantId, PeriodStart = day, PeriodEnd = day,
            ActiveStudents = students, ActiveEmployees = employees, ActiveCampuses = campuses,
            CreatedBy = user.UserId
        };
        db.UsageStatistics.Add(row);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, UserId = user.UserId, Action = "UsageSnapshotCaptured",
            EntityName = nameof(UsageStatistics), NewValue = day.ToString("yyyy-MM-dd"),
            OccurredAt = DateTime.UtcNow, IsSuccess = true
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<UsageStatisticsDto>.SuccessResponse(Map(row), "Daily usage snapshot captured.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Usage capture conflict for tenant {TenantId}", tenantId);
            var winner = await db.UsageStatistics.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && !x.IsDeleted && x.PeriodStart == day && x.PeriodEnd == day, ct);
            return winner != null
                ? ApiResponse<UsageStatisticsDto>.SuccessResponse(Map(winner), "Today's snapshot already exists.")
                : ApiResponse<UsageStatisticsDto>.ErrorResponse("Snapshot could not be saved.", 409);
        }
    }

    /// <summary>Bounded tenant-only export. Unmetered storage/SMS/email fields are intentionally excluded.</summary>
    public async Task<ApiResponse<string>> ExportHistoryCsvAsync(
        DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<string>.ErrorResponse("Tenant administrator required.", 403);
        var end = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (end.Year < 2000) return ApiResponse<string>.ErrorResponse("Invalid export dates.", 400);
        var start = fromDate ?? end.AddDays(-89);
        if (start > end || end.DayNumber - start.DayNumber > 365)
            return ApiResponse<string>.ErrorResponse("Select a date range of at most 366 days.", 400);
        var rows = await db.UsageStatistics.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.PeriodStart >= start && x.PeriodStart <= end)
            .OrderBy(x => x.PeriodStart).ThenBy(x => x.Id)
            .Take(1001).Select(x => new { x.PeriodStart, x.ActiveStudents, x.ActiveEmployees, x.ActiveCampuses })
            .ToListAsync(ct);
        if (rows.Count > 1000)
            return ApiResponse<string>.ErrorResponse("Too many snapshots; narrow the date range.", 413);
        var csv = new System.Text.StringBuilder("LocalDay,ActiveStudents,ActiveEmployees,ActiveCampuses\r\n");
        foreach (var row in rows)
            csv.Append(FormattableString.Invariant(
                $"{row.PeriodStart:yyyy-MM-dd},{row.ActiveStudents},{row.ActiveEmployees},{row.ActiveCampuses}\r\n"));
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
