using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

// Extends the canonical subscription-plan owner. Host-only operations never accept tenant IDs.
public partial class SubscriptionPlanService
{
    private static bool ValidMoney(decimal value) =>
        value >= 0m && value <= 9999999999999999.99m && decimal.Round(value, 2) == value;

    private static bool ValidLimits(SaveSubscriptionPlanConfigurationRequestDto request) =>
        request.TrialDays is >= 0 and <= 365 &&
        request.MaxStudents >= 0 && request.MaxEmployees >= 0 &&
        request.MaxUsers >= 0 && request.MaxCampuses >= 0 && request.MaxStorageMb >= 0;

    private static string Snapshot(decimal monthly, decimal yearly, int trial,
        int students, int employees, int users, int campuses, int storage,
        IEnumerable<SubscriptionPlanFeatureSelectionDto> features) =>
        JsonSerializer.Serialize(new
        {
            monthly, yearly, trial, students, employees, users, campuses, storage,
            features = features.OrderBy(x => x.FeatureId)
                .Select(x => new { x.FeatureId, x.IsEnabled, LimitValue = x.IsEnabled ? x.LimitValue : null })
                .ToArray()
        });

    private async Task<SubscriptionPlanConfigurationDto> MapConfigurationAsync(
        SubscriptionPlan plan, CancellationToken ct)
    {
        var db = _adminDb!;
        var catalog = await db.Features.AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted).OrderBy(x => x.Code).ToListAsync(ct);
        var existing = await db.PlanFeatures.AsNoTracking()
            .Where(x => x.SubscriptionPlanId == plan.Id && !x.IsDeleted).ToListAsync(ct);
        var lookup = existing.GroupBy(x => x.FeatureId).ToDictionary(x => x.Key, x => x.First());
        return new SubscriptionPlanConfigurationDto
        {
            Id = plan.Id, Code = plan.Code, Name = plan.Name, CurrencyCode = plan.CurrencyCode,
            MonthlyPrice = plan.MonthlyPrice, YearlyPrice = plan.YearlyPrice, TrialDays = plan.TrialDays,
            MaxStudents = plan.MaxStudents, MaxEmployees = plan.MaxEmployees, MaxUsers = plan.MaxUsers,
            MaxCampuses = plan.MaxCampuses, MaxStorageMb = plan.MaxStorageMb,
            RowVersion = Convert.ToBase64String(plan.RowVersion),
            Features = catalog.Select(x => new SubscriptionPlanFeatureConfigurationDto
            {
                FeatureId = x.Id, Code = x.Code, Name = x.Name,
                IsEnabled = lookup.TryGetValue(x.Id, out var assignment) && assignment.IsEnabled,
                LimitValue = lookup.TryGetValue(x.Id, out var configured) && configured.IsEnabled
                    ? configured.LimitValue : null
            }).ToList()
        };
    }

    public async Task<ApiResponse<SubscriptionPlanConfigurationDto>> GetPlanConfigurationAsync(
        long id, CancellationToken ct = default)
    {
        if (!CanAdministerPlans)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Platform administrator required.", 403);
        var plan = await _adminDb!.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (plan == null)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Plan not found.", 404);
        return ApiResponse<SubscriptionPlanConfigurationDto>.SuccessResponse(
            await MapConfigurationAsync(plan, ct));
    }

    public async Task<ApiResponse<SubscriptionPlanConfigurationDto>> SavePlanConfigurationAsync(
        long id, SaveSubscriptionPlanConfigurationRequestDto request, CancellationToken ct = default)
    {
        if (!CanAdministerPlans)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Platform administrator required.", 403);
        if (id <= 0 || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) ||
            !ValidMoney(request.MonthlyPrice) || !ValidMoney(request.YearlyPrice) ||
            !ValidLimits(request) || request.Features == null ||
            request.Features.Any(x => x.FeatureId <= 0 || x.LimitValue < 0 ||
                (!x.IsEnabled && x.LimitValue.HasValue)) ||
            request.Features.Select(x => x.FeatureId).Distinct().Count() != request.Features.Count)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Invalid pricing, limits or feature selection.", 400);

        byte[] supplied;
        try { supplied = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException)
        {
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Invalid row version.", 400);
        }
        if (supplied.Length == 0)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Invalid row version.", 400);

        var db = _adminDb!;
        var user = _adminUser!;
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (plan == null)
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse("Plan not found.", 404);

        var catalogIds = await db.Features.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => x.Id).ToListAsync(ct);
        if (catalogIds.Count != request.Features.Count ||
            catalogIds.Except(request.Features.Select(x => x.FeatureId)).Any())
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse(
                "Feature catalog changed. Refresh and retry.", 409);

        var desired = Snapshot(request.MonthlyPrice, request.YearlyPrice, request.TrialDays,
            request.MaxStudents, request.MaxEmployees, request.MaxUsers,
            request.MaxCampuses, request.MaxStorageMb, request.Features);
        var existing = await db.PlanFeatures.Where(x => x.SubscriptionPlanId == id && !x.IsDeleted)
            .ToListAsync(ct);
        var byFeature = existing.GroupBy(x => x.FeatureId).ToDictionary(x => x.Key, x => x.First());
        var currentSelections = catalogIds.Select(featureId =>
        {
            byFeature.TryGetValue(featureId, out var assignment);
            return new SubscriptionPlanFeatureSelectionDto
            {
                FeatureId = featureId, IsEnabled = assignment?.IsEnabled == true,
                LimitValue = assignment?.IsEnabled == true ? assignment.LimitValue : null
            };
        }).ToList();
        var previous = Snapshot(plan.MonthlyPrice, plan.YearlyPrice, plan.TrialDays,
            plan.MaxStudents, plan.MaxEmployees, plan.MaxUsers,
            plan.MaxCampuses, plan.MaxStorageMb, currentSelections);
        var requestId = request.RequestId.ToString("D");
        // Host audit records are not tenant-owned; this bypass is restricted to the
        // authenticated SuperAdmin host scope and a specific plan/request identifier.
        var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == null && !x.IsDeleted && x.EntityName == nameof(SubscriptionPlan) &&
            x.EntityId == id && x.Action == "SubscriptionPlan.ConfigurationChanged" &&
            x.RequestId == requestId, ct);
        if (prior != null)
            return prior.UserId == user.UserId && prior.NewValue == desired && previous == desired
                ? ApiResponse<SubscriptionPlanConfigurationDto>.SuccessResponse(
                    await MapConfigurationAsync(plan, ct), "Already applied.")
                : ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse(
                    "Request ID was already used or the plan changed.", 409);

        if (!plan.RowVersion.AsSpan().SequenceEqual(supplied))
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse(
                "Plan changed. Refresh and retry.", 409);
        if (previous == desired)
            return ApiResponse<SubscriptionPlanConfigurationDto>.SuccessResponse(
                await MapConfigurationAsync(plan, ct), "Already in requested state.");

        var now = DateTime.UtcNow;
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.YearlyPrice = request.YearlyPrice;
        plan.TrialDays = request.TrialDays;
        plan.MaxStudents = request.MaxStudents;
        plan.MaxEmployees = request.MaxEmployees;
        plan.MaxUsers = request.MaxUsers;
        plan.MaxCampuses = request.MaxCampuses;
        plan.MaxStorageMb = request.MaxStorageMb;
        plan.UpdatedAt = now; // Touch the plan row even for feature-only edits: SQL rowversion serializes writes.
        plan.UpdatedBy = user.UserId;
        foreach (var item in request.Features)
        {
            if (!byFeature.TryGetValue(item.FeatureId, out var assignment))
            {
                if (!item.IsEnabled) continue;
                assignment = new PlanFeature
                {
                    SubscriptionPlanId = id, FeatureId = item.FeatureId,
                    CreatedAt = now, CreatedBy = user.UserId
                };
                db.PlanFeatures.Add(assignment);
            }
            assignment.IsEnabled = item.IsEnabled;
            assignment.LimitValue = item.IsEnabled ? item.LimitValue : null;
            assignment.UpdatedAt = now;
            assignment.UpdatedBy = user.UserId;
        }
        var audit = new AuditLog
        {
            TenantId = null, UserId = user.UserId,
            Action = "SubscriptionPlan.ConfigurationChanged",
            EntityName = nameof(SubscriptionPlan), EntityId = id,
            OldValue = previous, NewValue = desired, RequestId = requestId,
            Endpoint = "/api/platform/subscription-plans",
            IsSuccess = true, OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
        };
        db.AuditLogs.Add(audit);
        try
        {
            await db.SaveChangesAsync(ct); // Relational SaveChanges is atomic: plan, assignments and audit.
            return ApiResponse<SubscriptionPlanConfigurationDto>.SuccessResponse(
                await MapConfigurationAsync(plan, ct), "Plan configuration saved.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse(
                "Plan changed. Refresh and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Subscription plan configuration conflict for {PlanId}", id);
            return ApiResponse<SubscriptionPlanConfigurationDto>.ErrorResponse(
                "Plan configuration conflicted with another operation.", 409);
        }
    }
}
