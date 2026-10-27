using System.Text.Json;
using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Platform;

/// <summary>Host-only tenant suspension/reactivation. Subscription and onboarding owners remain authoritative.</summary>
public sealed class PlatformTenantLifecycleService(
    IGenericRepository<Tenant> tenants,
    IGenericRepository<TenantSubscription> subscriptions,
    IGenericRepository<AuditLog> audits,
    IUnitOfWork unit,
    ICurrentUserService user,
    TimeProvider clock,
    ILogger<PlatformTenantLifecycleService> logger) : IPlatformTenantLifecycleService
{
    private bool CanManage => user.IsAuthenticated && user.IsSuperAdmin;
    public static bool IsAllowedTransition(TenantState current, TenantState target) =>
        (current == TenantState.Active && target == TenantState.Suspended) ||
        (current == TenantState.Suspended && target == TenantState.Active);

    private static PlatformTenantLifecycleDto Map(Tenant row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, Email = row.Email,
        State = row.State, OnboardingStage = row.OnboardingStage,
        EmailVerified = row.EmailVerifiedAt.HasValue,
        OnboardingComplete = row.OnboardingStage == OnboardingStage.Completed && row.OnboardingCompletedAt.HasValue,
        CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static bool MatchesVersion(byte[] current, string encoded)
    {
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<PagedResult<PlatformTenantLifecycleDto>>> SearchAsync(
        string? search, TenantState? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<PlatformTenantLifecycleDto>>.ErrorResponse("Platform administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            search?.Length > 100 || (state.HasValue && !Enum.IsDefined(state.Value)))
            return ApiResponse<PagedResult<PlatformTenantLifecycleDto>>.ErrorResponse("Invalid search or paging parameters.");
        try
        {
            var query = tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking().Where(x => !x.IsDeleted);
            if (state.HasValue) query = query.Where(x => x.State == state.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.Email.Contains(term));
            }
            var count = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<PlatformTenantLifecycleDto>>.SuccessResponse(new()
            {
                Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform tenant directory failed.");
            return ApiResponse<PagedResult<PlatformTenantLifecycleDto>>.ErrorResponse("Tenant directory is unavailable.", 500);
        }
    }

    private static PlatformTenantStateHistoryDto MapStateHistory(AuditLog row)
    {
        var target = "Unknown";
        var reason = "Unavailable";
        try
        {
            using var json = JsonDocument.Parse(row.NewValue ?? "{}");
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("State", out var state) && state.ValueKind == JsonValueKind.String)
                    target = state.GetString() ?? target;
                if (json.RootElement.TryGetProperty("Reason", out var value) && value.ValueKind == JsonValueKind.String)
                    reason = value.GetString() ?? reason;
            }
        }
        catch (JsonException) { /* Older malformed audit payloads remain visible without raw JSON. */ }
        return new PlatformTenantStateHistoryDto
        {
            Id = row.Id, OccurredAt = row.OccurredAt,
            Actor = row.UserName ?? (row.UserId.HasValue ? $"User #{row.UserId}" : "System"),
            PreviousState = row.OldValue ?? "Unknown", TargetState = target,
            Reason = reason, RequestId = row.RequestId
        };
    }

    public async Task<ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>> GetStateHistoryAsync(
        long tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage)
            return ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>.ErrorResponse("Platform administrator required.", 403);
        if (tenantId <= 0 || page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>.ErrorResponse("Invalid institution or paging parameters.");
        try
        {
            var found = await tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.Id == tenantId && !x.IsDeleted, ct);
            if (!found)
                return ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>.ErrorResponse("Institution not found.", 404);
            // IgnoreQueryFilters is restricted to this host-only service. Every audit row
            // must match tenant, canonical entity ID, action, and successful state change.
            var query = audits.GetQueryable().IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.TenantId == tenantId && x.EntityName == "Tenant" && x.EntityId == tenantId &&
                x.Action == "StateChange" && x.IsSuccess && !x.IsDeleted);
            var count = await query.CountAsync(ct);
            var history = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>.SuccessResponse(new()
            {
                Items = history.Select(MapStateHistory).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform state history failed for tenant {TenantId}", tenantId);
            return ApiResponse<PagedResult<PlatformTenantStateHistoryDto>>.ErrorResponse("Institution history is unavailable.", 500);
        }
    }

    public async Task<ApiResponse<PlatformTenantLifecycleDto>> ChangeStateAsync(
        long tenantId, ChangePlatformTenantStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Platform administrator required.", 403);
        if (tenantId <= 0 || request == null || request.RequestId == Guid.Empty ||
            request.TargetState is not (TenantState.Active or TenantState.Suspended) ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 500)
            return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("State, reason, version and request ID are required.");
        if (user.TenantId > 0 && tenantId == user.TenantId)
            return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Cannot change the host institution from its own session.", 403);
        var reason = request.Reason.Trim();
        var requestId = request.RequestId.ToString("D");
        var auditPayload = JsonSerializer.Serialize(new { State = request.TargetState.ToString(), Reason = reason });
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var tenant = await tenants.GetQueryable().IgnoreQueryFilters().FirstOrDefaultAsync(x =>
                    x.Id == tenantId && !x.IsDeleted, token);
                if (tenant == null) return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Institution not found.", 404);

                var prior = await audits.GetQueryable().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.EntityName == "Tenant" && x.EntityId == tenantId &&
                    x.Action == "StateChange" && x.RequestId == requestId && !x.IsDeleted, token);
                if (prior != null)
                    return prior.UserId == user.UserId && prior.NewValue == auditPayload && tenant.State == request.TargetState
                        ? ApiResponse<PlatformTenantLifecycleDto>.SuccessResponse(Map(tenant), "Already applied.")
                        : ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Request ID was already used or state changed again.", 409);

                if (!MatchesVersion(tenant.RowVersion, request.RowVersion))
                    return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Institution changed. Reload and retry.", 409);
                if (!IsAllowedTransition(tenant.State, request.TargetState))
                    return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Only Active to Suspended and Suspended to Active transitions are supported.", 409);

                var now = clock.GetUtcNow().UtcDateTime;
                if (request.TargetState == TenantState.Active)
                {
                    if (!tenant.EmailVerifiedAt.HasValue || tenant.OnboardingStage != OnboardingStage.Completed ||
                        !tenant.OnboardingCompletedAt.HasValue)
                        return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Email verification and onboarding must be complete before reactivation.", 409);
                    var entitled = await subscriptions.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenantId && !x.IsDeleted && x.StartsAt <= now && x.EndsAt > now &&
                        (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                         x.State == SubscriptionState.Grace), token);
                    if (!entitled)
                        return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("A valid active subscription is required before reactivation.", 409);
                }
                var previous = tenant.State;
                tenant.State = request.TargetState;
                tenant.UpdatedAt = now;
                tenant.UpdatedBy = user.UserId;
                await audits.AddAsync(new AuditLog
                {
                    TenantId = tenantId, UserId = user.UserId,
                    UserName = (user.FullName ?? user.Email ?? "SuperAdmin")[..Math.Min(200, (user.FullName ?? user.Email ?? "SuperAdmin").Length)],
                    Action = "StateChange", EntityName = "Tenant", EntityId = tenantId,
                    OldValue = previous.ToString(), NewValue = auditPayload,
                    RequestId = requestId, IpAddress = user.IpAddress?[..Math.Min(100, user.IpAddress.Length)],
                    Endpoint = "/api/platform/tenants/state", IsSuccess = true,
                    OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
                });
                await unit.SaveChangesAsync(token);
                return ApiResponse<PlatformTenantLifecycleDto>.SuccessResponse(Map(tenant), "Institution state updated.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Institution changed. Reload and retry.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Institution update conflicted with another operation.", 409); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform tenant state transition failed for {TenantId}", tenantId);
            return ApiResponse<PlatformTenantLifecycleDto>.ErrorResponse("Institution state could not be updated.", 500);
        }
    }
}
