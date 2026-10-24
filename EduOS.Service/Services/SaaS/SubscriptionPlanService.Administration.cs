using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Host-only lifecycle for the existing subscription-plan aggregate.
public partial class SubscriptionPlanService
{
    private EduOSDbContext? _adminDb;
    private ICurrentUserService? _adminUser;

    // Preserve the existing five-argument constructor used by public catalog consumers/tests.
    // DI selects this longer constructor for authenticated host operations.
    public SubscriptionPlanService(ISubscriptionPlanRepository plans,
        IGenericRepository<PlanFeature> planFeatures, IGenericRepository<Feature> features,
        IMapper mapper, ILogger<SubscriptionPlanService> logger,
        EduOSDbContext db, ICurrentUserService user)
        : this(plans, planFeatures, features, mapper, logger)
    {
        _adminDb = db;
        _adminUser = user;
    }

    private bool CanAdministerPlans =>
        _adminDb != null && _adminUser?.IsAuthenticated == true && _adminUser.IsSuperAdmin;

    private static SubscriptionPlanAvailabilityDto MapAvailability(SubscriptionPlan plan) => new()
    {
        Id = plan.Id, Code = plan.Code, Name = plan.Name,
        MonthlyPrice = plan.MonthlyPrice, YearlyPrice = plan.YearlyPrice,
        CurrencyCode = plan.CurrencyCode, IsActive = plan.IsActive, IsPublic = plan.IsPublic,
        RowVersion = Convert.ToBase64String(plan.RowVersion)
    };

    public async Task<ApiResponse<IReadOnlyList<SubscriptionPlanAvailabilityDto>>> GetAdminPlanAvailabilityAsync(
        CancellationToken ct = default)
    {
        if (!CanAdministerPlans)
            return ApiResponse<IReadOnlyList<SubscriptionPlanAvailabilityDto>>.ErrorResponse("Platform administrator required.", 403);
        var plans = await _adminDb!.SubscriptionPlans.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.Id).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SubscriptionPlanAvailabilityDto>>.SuccessResponse(
            plans.Select(MapAvailability).ToList());
    }

    public async Task<ApiResponse<SubscriptionPlanAvailabilityDto>> ChangePlanAvailabilityAsync(
        long id, ChangeSubscriptionPlanAvailabilityRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerPlans)
            return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse("Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse(
                "Valid plan, request ID and row version are required.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        var requestId = request.RequestId.ToString("D");
        var target = (request.IsActive ? "Active" : "Inactive") + "|" +
                     (request.IsPublic ? "Public" : "Hidden");
        SubscriptionPlan? plan = null;
        AuditLog? audit = null;
        try
        {
            // Plan is host-owned; do not bypass the plan's normal soft-delete filter.
            plan = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
            if (plan == null)
                return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse("Plan not found.", 404);

            // Host audit records have no tenant ID and require an explicit host-only lookup.
            var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(SubscriptionPlan) &&
                x.EntityId == id && x.Action == "SubscriptionPlan.AvailabilityChanged" &&
                x.RequestId == requestId, ct);
            if (prior != null)
                return prior.UserId == user.UserId && prior.NewValue == target &&
                       plan.IsActive == request.IsActive && plan.IsPublic == request.IsPublic
                    ? ApiResponse<SubscriptionPlanAvailabilityDto>.SuccessResponse(MapAvailability(plan), "Already applied.")
                    : ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse(
                        "Request ID was already used or the plan state changed.", 409);

            byte[] supplied;
            try { supplied = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException)
            {
                return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse("Invalid row version.", 400);
            }
            if (supplied.Length == 0 || !plan.RowVersion.AsSpan().SequenceEqual(supplied))
                return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse(
                    "Plan changed. Refresh and retry.", 409);

            if (plan.IsActive == request.IsActive && plan.IsPublic == request.IsPublic)
                return ApiResponse<SubscriptionPlanAvailabilityDto>.SuccessResponse(
                    MapAvailability(plan), "Already in requested state.");

            var now = DateTime.UtcNow;
            var previous = (plan.IsActive ? "Active" : "Inactive") + "|" +
                           (plan.IsPublic ? "Public" : "Hidden");
            plan.IsActive = request.IsActive;
            plan.IsPublic = request.IsPublic;
            plan.UpdatedAt = now;
            plan.UpdatedBy = user.UserId;
            audit = new AuditLog
            {
                TenantId = null, UserId = user.UserId,
                Action = "SubscriptionPlan.AvailabilityChanged",
                EntityName = nameof(SubscriptionPlan), EntityId = id,
                OldValue = previous, NewValue = target, RequestId = requestId,
                Endpoint = "/api/platform/subscription-plans",
                IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
            };
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync(ct);
            return ApiResponse<SubscriptionPlanAvailabilityDto>.SuccessResponse(
                MapAvailability(plan), "Subscription plan availability updated.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            if (audit != null) db.Entry(audit).State = EntityState.Detached;
            if (plan != null) db.Entry(plan).State = EntityState.Detached;
            return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse(
                "Plan changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (audit != null) db.Entry(audit).State = EntityState.Detached;
            if (plan != null) db.Entry(plan).State = EntityState.Detached;
            _logger.LogWarning(ex, "Subscription plan availability conflict for {PlanId}", id);
            return ApiResponse<SubscriptionPlanAvailabilityDto>.ErrorResponse(
                "Plan update conflicted with another operation.", 409);
        }
    }
}
