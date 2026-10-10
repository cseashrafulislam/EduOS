using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.SaaS;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ISubscriptionInvoiceRepository _invoices;
    private readonly IGenericRepository<SubscriptionInvoiceLine> _invoiceLines;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(ITenantSubscriptionRepository subscriptions, ISubscriptionPlanRepository plans,
        ISubscriptionInvoiceRepository invoices, IGenericRepository<SubscriptionInvoiceLine> invoiceLines,
        IGenericRepository<Tenant> tenants, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<SubscriptionService> logger)
    {
        _subscriptions = subscriptions;
        _plans = plans;
        _invoices = invoices;
        _invoiceLines = invoiceLines;
        _tenants = tenants;
        _uow = uow;
        _user = user;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<TenantSubscriptionDto>> StartAsync(StartSubscriptionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TenantSubscriptionDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.SubscriptionPlanId <= 0 ||
            !SubscriptionCalculator.IsSupportedCycle(request.BillingCycleCode))
            return Error<TenantSubscriptionDto>("A valid plan, billing cycle and client request ID are required.");
        var tenantId = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var previous = await _subscriptions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.PublicId == request.ClientRequestId && !x.IsDeleted, token);
                if (previous != null)
                {
                    if (previous.SubscriptionPlanId != request.SubscriptionPlanId ||
                        previous.BillingCycleCode != request.BillingCycleCode)
                        return Error<TenantSubscriptionDto>("Client request ID was used with different subscription details.", 409);
                    return ApiResponse<TenantSubscriptionDto>.SuccessResponse(await MapAsync(previous, token),
                        "Subscription request already processed.");
                }
                var tenant = await _tenants.GetQueryable().FirstOrDefaultAsync(x =>
                    x.Id == tenantId && !x.IsDeleted && x.State != TenantState.Closed, token);
                if (tenant == null) return Error<TenantSubscriptionDto>("Institution not found.", 404);
                if (!tenant.EmailVerifiedAt.HasValue)
                    return Error<TenantSubscriptionDto>("Verify institution email before starting subscription.", 409);
                var plan = await _plans.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == request.SubscriptionPlanId && x.IsActive && x.IsPublic && !x.IsDeleted, token);
                if (plan == null) return Error<TenantSubscriptionDto>("Subscription plan not available.", 404);
                var now = _clock.GetUtcNow().UtcDateTime;
                var hasCurrent = await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && !x.IsDeleted &&
                    (x.State == SubscriptionState.PendingPayment ||
                     ((x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                       x.State == SubscriptionState.Grace) && x.EndsAt > now)), token);
                if (hasCurrent)
                    return Error<TenantSubscriptionDto>("A current or pending subscription already exists.", 409);
                var hasTrial = await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.IsTrial && !x.IsDeleted, token);
                var trial = request.StartTrialIfEligible && plan.TrialDays > 0 && !hasTrial;
                var amount = trial ? 0m : SubscriptionCalculator.GetPriceForCycle(plan, request.BillingCycleCode);
                if (amount < 0m) return Error<TenantSubscriptionDto>("Subscription plan price is invalid.", 409);
                var state = trial ? SubscriptionState.Trial :
                    amount == 0m ? SubscriptionState.Active : SubscriptionState.PendingPayment;
                var row = new TenantSubscription
                {
                    PublicId = request.ClientRequestId, TenantId = tenantId, SubscriptionPlanId = plan.Id,
                    BillingCycleCode = request.BillingCycleCode, State = state, IsTrial = trial,
                    StartsAt = now,
                    EndsAt = trial ? now.AddDays(plan.TrialDays) :
                        SubscriptionCalculator.CalculateEndDate(now, request.BillingCycleCode),
                    AutoRenew = false, PriceSnapshot = amount, CurrencyCode = plan.CurrencyCode,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _subscriptions.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                if (amount > 0m)
                {
                    // Client request UUID is an immutable, concurrency-safe invoice suffix.
                    var invoice = new SubscriptionInvoice
                    {
                        TenantId = tenantId, TenantSubscriptionId = row.Id,
                        InvoiceNumber = "INV-" + now.ToString("yyyyMM") + "-" + request.ClientRequestId.ToString("N"),
                        InvoiceDate = DateOnly.FromDateTime(now), DueDate = DateOnly.FromDateTime(now.AddDays(7)),
                        Subtotal = amount, TaxAmount = 0m, TotalAmount = amount, PaidAmount = 0m,
                        DueAmount = amount, CurrencyCode = plan.CurrencyCode, State = InvoiceState.Issued,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _invoices.AddAsync(invoice);
                    await _uow.SaveChangesAsync(token);
                    await _invoiceLines.AddAsync(new SubscriptionInvoiceLine
                    {
                        TenantId = tenantId, SubscriptionInvoiceId = invoice.Id,
                        Description = plan.Name + " / " + request.BillingCycleCode,
                        Quantity = 1m, UnitPrice = amount, Amount = amount,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    await _uow.SaveChangesAsync(token);
                }
                if (tenant.OnboardingStage == OnboardingStage.PlanSelection)
                    tenant.OnboardingStage = OnboardingStage.Payment;
                tenant.UpdatedAt = now;
                tenant.UpdatedBy = _user.UserId;
                _tenants.Update(tenant);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<TenantSubscriptionDto>.SuccessResponse(
                    await MapAsync(row, token), trial ? "Trial subscription started." :
                    amount == 0m ? "Subscription activated." : "Subscription invoice issued; payment required.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Subscription write conflict for tenant {TenantId}", tenantId);
            return Error<TenantSubscriptionDto>("Subscription conflicts with another request. Reload and retry.", 409);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Subscription concurrency conflict for tenant {TenantId}", tenantId);
            return Error<TenantSubscriptionDto>("Subscription was changed. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<TenantSubscriptionDto?>> GetCurrentAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<TenantSubscriptionDto?>();
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await _subscriptions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                (x.State == SubscriptionState.PendingPayment || x.State == SubscriptionState.Suspended ||
                 x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                 x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        return ApiResponse<TenantSubscriptionDto?>.SuccessResponse(row == null ? null : await MapAsync(row, ct));
    }

    public async Task<ApiResponse<PagedResult<TenantSubscriptionDto>>> GetHistoryAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<PagedResult<TenantSubscriptionDto>>();
        if (page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<TenantSubscriptionDto>>("Page must be positive and page size at most 100.");
        var q = _subscriptions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && !x.IsDeleted);
        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<TenantSubscriptionDto>>("Page exceeds result range.");
        var rows = await q.OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        var planIds = rows.Select(x => x.SubscriptionPlanId).Distinct().ToArray();
        var plans = await _plans.GetQueryable().AsNoTracking().Where(x => planIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var result = new PagedResult<TenantSubscriptionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x => Map(x, plans.GetValueOrDefault(x.SubscriptionPlanId))).ToList()
        };
        return ApiResponse<PagedResult<TenantSubscriptionDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<TenantSubscriptionDto>> CancelAsync(Guid subscriptionReference,
        CancelSubscriptionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TenantSubscriptionDto>();
        if (subscriptionReference == Guid.Empty || request == null ||
            !TryVersion(request.RowVersion, out var version))
            return Error<TenantSubscriptionDto>("Subscription reference and row version are required.");
        if (!string.IsNullOrWhiteSpace(request.Reason))
            return Error<TenantSubscriptionDto>("Cancellation reason cannot be retained by the current subscription model.", 409);
        return await ChangeAsync(subscriptionReference, version, row =>
        {
            if (row.State is SubscriptionState.Expired or SubscriptionState.Cancelled)
                return "Subscription has already ended.";
            row.AutoRenew = false;
            if (!request.AtPeriodEnd)
            {
                row.State = SubscriptionState.Cancelled;
                row.CancelledAt = _clock.GetUtcNow().UtcDateTime;
                row.EndsAt = row.CancelledAt.Value;
            }
            return null;
        }, ct);
    }

    public async Task<ApiResponse<TenantSubscriptionDto>> SetAutoRenewAsync(Guid subscriptionReference,
        UpdateSubscriptionAutoRenewRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TenantSubscriptionDto>();
        if (subscriptionReference == Guid.Empty || request == null ||
            !TryVersion(request.RowVersion, out var version))
            return Error<TenantSubscriptionDto>("Subscription reference and row version are required.");
        return await ChangeAsync(subscriptionReference, version, row =>
        {
            if (row.State is not (SubscriptionState.Active or SubscriptionState.Trial or SubscriptionState.Grace) ||
                row.EndsAt <= _clock.GetUtcNow().UtcDateTime)
                return "Auto-renew cannot be changed for this subscription.";
            row.AutoRenew = request.AutoRenew;
            return null;
        }, ct);
    }

    private async Task<ApiResponse<TenantSubscriptionDto>> ChangeAsync(Guid reference, byte[] version,
        Func<TenantSubscription, string?> apply, CancellationToken ct)
    {
        try
        {
            var row = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (row == null) return Error<TenantSubscriptionDto>("Subscription not found.", 404);
            if (row.RowVersion.Length != version.Length ||
                !CryptographicOperations.FixedTimeEquals(row.RowVersion, version))
                return Error<TenantSubscriptionDto>("Subscription has changed. Reload and retry.", 409);
            var reason = apply(row);
            if (reason != null) return Error<TenantSubscriptionDto>(reason, 409);
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = _user.UserId;
            _subscriptions.Update(row);
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<TenantSubscriptionDto>.SuccessResponse(await MapAsync(row, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<TenantSubscriptionDto>("Subscription changed concurrently.", 409);
        }
    }

    public async Task<ApiResponse<bool>> ActivateAfterPaymentAsync(long subscriptionId, long tenantId,
        CancellationToken ct = default)
    {
        if (subscriptionId <= 0 || tenantId <= 0) return Error<bool>("Subscription reference is invalid.");
        try
        {
            var paid = await _invoices.HasFullyPaidInvoiceForSubscriptionAsync(tenantId, subscriptionId, ct);
            if (!paid) return Error<bool>("Subscription invoice is not fully settled.", 409);
            var row = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
                x.Id == subscriptionId && x.TenantId == tenantId && !x.IsDeleted, ct);
            if (row == null) return Error<bool>("Subscription not found.", 404);
            if (row.State == SubscriptionState.Active) return ApiResponse<bool>.SuccessResponse(true);
            if (row.State != SubscriptionState.PendingPayment)
                return Error<bool>("Subscription cannot be activated from its current state.", 409);
            row.State = SubscriptionState.Active;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            _subscriptions.Update(row);
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Subscription changed concurrently.", 409); }
    }

    public async Task<ApiResponse<bool>> CheckExpiryAsync(long tenantId, CancellationToken ct = default)
    {
        if (tenantId <= 0) return Error<bool>("Tenant reference is invalid.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && !x.IsDeleted && x.EndsAt <= now &&
            (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
             x.State == SubscriptionState.Grace), ct);
        if (row == null) return ApiResponse<bool>.SuccessResponse(true);
        row.State = SubscriptionState.Expired;
        row.UpdatedAt = now;
        _subscriptions.Update(row);
        try { await _uow.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Subscription changed concurrently.", 409); }
        return ApiResponse<bool>.SuccessResponse(true, "Subscription expired.");
    }

    private async Task<TenantSubscriptionDto> MapAsync(TenantSubscription row, CancellationToken ct)
    {
        var name = await _plans.GetQueryable().AsNoTracking().Where(x => x.Id == row.SubscriptionPlanId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        return Map(row, name);
    }

    private static TenantSubscriptionDto Map(TenantSubscription row, string? planName) => new()
    {
        Id = row.Id, Reference = row.PublicId, SubscriptionPlanId = row.SubscriptionPlanId,
        BillingCycleCode = row.BillingCycleCode, PlanName = planName ?? string.Empty,
        State = row.State, IsTrial = row.IsTrial, PriceSnapshot = row.PriceSnapshot,
        CurrencyCode = row.CurrencyCode, StartsAt = row.StartsAt, EndsAt = row.EndsAt,
        CancelledAt = row.CancelledAt, AutoRenew = row.AutoRenew,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static bool TryVersion(string? value, out byte[] result)
    {
        result = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { result = Convert.FromBase64String(value); return result.Length > 0; }
        catch (FormatException) { return false; }
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
    private static ApiResponse<T> Denied<T>() => Error<T>("Tenant administrator access is required.", 403);
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
}
