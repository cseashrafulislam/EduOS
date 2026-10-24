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
        return await CaptureForTenantAsync(db, user.TenantId, user.UserId, DateTime.UtcNow, logger, ct);
    }

    /// <summary>
    /// Shared writer for manual and trusted scheduled capture. The caller must already have
    /// established the same tenant on this DbContext; this method never bypasses tenant filters.
    /// The unique (TenantId, PeriodStart, PeriodEnd) index arbitrates concurrent retries.
    /// </summary>
    public static async Task<ApiResponse<UsageStatisticsDto>> CaptureForTenantAsync(
        EduOSDbContext db, long tenantId, long? actorId, DateTime utcNow,
        ILogger logger, CancellationToken ct = default)
    {
        if (tenantId <= 0 || db.CurrentTenantId != tenantId)
            return ApiResponse<UsageStatisticsDto>.ErrorResponse("Tenant scope mismatch.", 403);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == tenantId && !x.IsDeleted, ct);
        if (tenant == null) return ApiResponse<UsageStatisticsDto>.ErrorResponse("Institution not found.", 404);
        var day = ResolveLocalDay(utcNow, tenant.TimeZoneId, logger, tenantId, out _);
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
            CreatedBy = actorId
        };
        db.UsageStatistics.Add(row);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, UserId = actorId,
            Action = actorId.HasValue ? "UsageSnapshotCaptured" : "UsageSnapshotCapturedAutomatically",
            EntityName = nameof(UsageStatistics), NewValue = day.ToString("yyyy-MM-dd"),
            OccurredAt = utcNow, IsSuccess = true
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<UsageStatisticsDto>.SuccessResponse(Map(row), "Daily usage snapshot captured.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Usage capture conflict for tenant {TenantId}", tenantId);
            // A second worker may have won the unique-key race. Do not overwrite its snapshot.
            var winner = await db.UsageStatistics.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && !x.IsDeleted && x.PeriodStart == day && x.PeriodEnd == day, ct);
            return winner != null
                ? ApiResponse<UsageStatisticsDto>.SuccessResponse(Map(winner), "Today's snapshot already exists.")
                : ApiResponse<UsageStatisticsDto>.ErrorResponse("Snapshot could not be saved.", 409);
        }
    }

    private static DateOnly ResolveLocalDay(DateTime utcNow, string? configuredZone, ILogger logger,
        long tenantId, out string effectiveZone)
    {
        effectiveZone = "UTC";
        if (string.IsNullOrWhiteSpace(configuredZone)) return DateOnly.FromDateTime(utcNow);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(configuredZone);
            effectiveZone = zone.Id;
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Unknown tenant timezone {TenantId}; UTC used.", tenantId);
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("Invalid tenant timezone {TenantId}; UTC used.", tenantId);
        }
        return DateOnly.FromDateTime(utcNow);
    }

    public async Task<ApiResponse<UsageCaptureAutomationDto>> GetAutomationStatusAsync(CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<UsageCaptureAutomationDto>.ErrorResponse("Tenant administrator required.", 403);
        var tenantId = user.TenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == tenantId && !x.IsDeleted, ct);
        if (tenant == null) return ApiResponse<UsageCaptureAutomationDto>.ErrorResponse("Institution not found.", 404);
        var localDay = ResolveLocalDay(DateTime.UtcNow, tenant.TimeZoneId, logger, tenantId, out var effectiveZone);
        var lastDay = await db.UsageStatistics.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.Id)
            .Select(x => (DateOnly?)x.PeriodStart).FirstOrDefaultAsync(ct);
        return ApiResponse<UsageCaptureAutomationDto>.SuccessResponse(new()
        {
            LocalDay = localDay, LastCapturedDay = lastDay,
            CapturedToday = lastDay == localDay, EffectiveTimeZoneId = effectiveZone
        });
    }

    /// <summary>Compares the last saved tenant snapshot with the currently effective plan.
    /// Zero plan limits mean unlimited. No snapshot or plan means no inferred usage/limit.</summary>
    public async Task<ApiResponse<UsageQuotaOverviewDto>> GetQuotaOverviewAsync(CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<UsageQuotaOverviewDto>.ErrorResponse("Tenant administrator required.", 403);
        var tenantId = user.TenantId;
        var now = DateTime.UtcNow;
        var snapshot = await db.UsageStatistics.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.Id)
            .Select(x => new { x.PeriodStart, x.ActiveStudents, x.ActiveEmployees, x.ActiveCampuses })
            .FirstOrDefaultAsync(ct);
        var subscription = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted &&
                (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                 x.State == SubscriptionState.Grace) &&
                x.StartsAt <= now && x.EndsAt > now)
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.SubscriptionPlanId, x.State, x.EndsAt })
            .FirstOrDefaultAsync(ct);
        var plan = subscription == null ? null : await db.SubscriptionPlans.AsNoTracking()
            .Where(x => x.Id == subscription.SubscriptionPlanId && !x.IsDeleted)
            .Select(x => new { x.Name, x.MaxStudents, x.MaxEmployees, x.MaxCampuses })
            .FirstOrDefaultAsync(ct);
        var result = new UsageQuotaOverviewDto
        {
            HasActiveSubscription = subscription != null,
            PlanName = plan?.Name ?? (subscription == null ? "No current subscription" : "Plan unavailable"),
            SubscriptionStatus = subscription?.State.ToString() ?? "None",
            SubscriptionEndsAt = subscription?.EndsAt,
            SnapshotDay = snapshot?.PeriodStart,
            Metrics = snapshot == null ? Array.Empty<UsageQuotaMetricDto>() :
                new[]
                {
                    UsageQuotaMetricDto.FromCount("Active students", snapshot.ActiveStudents, plan?.MaxStudents),
                    UsageQuotaMetricDto.FromCount("Active employees", snapshot.ActiveEmployees, plan?.MaxEmployees),
                    UsageQuotaMetricDto.FromCount("Active campuses", snapshot.ActiveCampuses, plan?.MaxCampuses)
                }
        };
        return ApiResponse<UsageQuotaOverviewDto>.SuccessResponse(result);
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
