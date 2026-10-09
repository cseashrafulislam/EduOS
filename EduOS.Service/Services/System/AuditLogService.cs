using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services;

public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly ICurrentUserService _user;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IAuditLogRepository repository, ICurrentUserService user, ILogger<AuditLogService> logger)
    {
        _repository = repository;
        _user = user;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<AuditLogDto>>> SearchAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<AuditLogDto>>.ErrorResponse("Tenant audit permission is required.", 403);
        if (filter == null) return ApiResponse<PagedResult<AuditLogDto>>.ErrorResponse("A valid audit filter is required.");
        if (!ValidRange(filter)) return ApiResponse<PagedResult<AuditLogDto>>.ErrorResponse("UTC date range is invalid.");
        try
        {
            var query = FilterQuery(filter);
            var count = await query.CountAsync(cancellationToken);
            var offset = ((long)filter.Page - 1) * filter.PageSize;
            var items = offset > int.MaxValue ? new List<AuditLogDto>() : await query
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((int)offset).Take(filter.PageSize)
                .Select(x => new AuditLogDto
                {
                    Id = x.Id, TenantId = x.TenantId, UserId = x.UserId, UserName = x.UserName,
                    Action = x.Action, EntityName = x.EntityName, EntityId = x.EntityId,
                    IpAddress = x.IpAddress, Endpoint = x.Endpoint, IsSuccess = x.IsSuccess,
                    OccurredAt = x.OccurredAt, CorrelationId = x.CorrelationId, RequestId = x.RequestId
                }).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<AuditLogDto>>.SuccessResponse(new PagedResult<AuditLogDto>
            {
                Items = items, TotalCount = count, Page = filter.Page, PageSize = filter.PageSize
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant audit query failed for {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<AuditLogDto>>.ErrorResponse("Could not load audit records.", 500);
        }
    }

    public async Task<ApiResponse<AuditLogStatisticsDto>> GetStatisticsAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<AuditLogStatisticsDto>.ErrorResponse("Tenant audit permission is required.", 403);
        if (filter == null || !ValidRange(filter)) return ApiResponse<AuditLogStatisticsDto>.ErrorResponse("UTC date range is invalid.");
        try
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var week = now.AddDays(-7);
            var month = now.AddMonths(-1);
            var query = FilterQuery(filter);
            var totals = await query.GroupBy(x => 1).Select(g => new
            {
                Total = g.Count(), Today = g.Count(x => x.OccurredAt >= today),
                Week = g.Count(x => x.OccurredAt >= week),
                Month = g.Count(x => x.OccurredAt >= month),
                Created = g.Count(x => x.Action == "Create"),
                Updated = g.Count(x => x.Action == "Update"),
                Deleted = g.Count(x => x.Action == "Delete"),
                Successful = g.Count(x => x.IsSuccess),
                Failed = g.Count(x => !x.IsSuccess)
            }).FirstOrDefaultAsync(cancellationToken);
            var entities = await query.GroupBy(x => x.EntityName)
                .Select(g => new AuditEntityActivityDto { EntityName = g.Key, ActivityCount = g.LongCount() })
                .OrderByDescending(x => x.ActivityCount).ThenBy(x => x.EntityName).Take(10)
                .ToListAsync(cancellationToken);
            var users = await query.Where(x => x.UserId.HasValue)
                .GroupBy(x => new { x.UserId, x.UserName })
                .Select(g => new AuditUserActivityDto
                {
                    UserId = g.Key.UserId ?? 0, UserName = g.Key.UserName ?? string.Empty,
                    ActivityCount = g.LongCount()
                }).OrderByDescending(x => x.ActivityCount).ThenBy(x => x.UserId)
                .Take(10).ToListAsync(cancellationToken);
            return ApiResponse<AuditLogStatisticsDto>.SuccessResponse(new AuditLogStatisticsDto
            {
                TotalLogs = totals?.Total ?? 0, TodayLogs = totals?.Today ?? 0,
                ThisWeekLogs = totals?.Week ?? 0, ThisMonthLogs = totals?.Month ?? 0,
                CreateActions = totals?.Created ?? 0, UpdateActions = totals?.Updated ?? 0,
                DeleteActions = totals?.Deleted ?? 0, SuccessfulOperations = totals?.Successful ?? 0,
                FailedOperations = totals?.Failed ?? 0, TopEntities = entities, TopUsers = users
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant audit statistics failed for {TenantId}", _user.TenantId);
            return ApiResponse<AuditLogStatisticsDto>.ErrorResponse("Could not load audit statistics.", 500);
        }
    }

    private IQueryable<AuditLog> FilterQuery(AuditLogFilterDto filter)
    {
        IQueryable<AuditLog> query = _repository.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId);
        if (filter.UserId.HasValue) query = query.Where(x => x.UserId == filter.UserId);
        if (!string.IsNullOrWhiteSpace(filter.EntityName)) query = query.Where(x => x.EntityName == filter.EntityName.Trim());
        if (filter.EntityId.HasValue) query = query.Where(x => x.EntityId == filter.EntityId);
        if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(x => x.Action == filter.Action.Trim());
        if (filter.FromUtc.HasValue) query = query.Where(x => x.OccurredAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue) query = query.Where(x => x.OccurredAt <= filter.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(filter.IpAddress)) query = query.Where(x => x.IpAddress == filter.IpAddress.Trim());
        if (filter.IsSuccess.HasValue) query = query.Where(x => x.IsSuccess == filter.IsSuccess.Value);
        return query;
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal"));

    private static bool ValidRange(AuditLogFilterDto filter) =>
        (!filter.FromUtc.HasValue || filter.FromUtc.Value.Kind == DateTimeKind.Utc) &&
        (!filter.ToUtc.HasValue || filter.ToUtc.Value.Kind == DateTimeKind.Utc) &&
        (!filter.FromUtc.HasValue || !filter.ToUtc.HasValue || filter.FromUtc <= filter.ToUtc);
}
