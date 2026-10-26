using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Helpers.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.SaaS;

/// <summary>Schedules a separate paid period, preserving the predecessor's history.
/// Existing verified payment processing activates the successor; future StartsAt
/// prevents premature access.</summary>
public sealed partial class SubscriptionService
{
    public async Task<ApiResponse<TenantSubscriptionDto?>> GetUpcomingAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<TenantSubscriptionDto?>();
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await _subscriptions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                x.PreviousSubscriptionId.HasValue && x.EndsAt > now &&
                (x.State == SubscriptionState.PendingPayment ||
                 (x.State == SubscriptionState.Active && x.StartsAt > now)))
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return ApiResponse<TenantSubscriptionDto?>.SuccessResponse(
            row == null ? null : await MapAsync(row, ct));
    }

    public async Task<ApiResponse<TenantSubscriptionDto>> RenewAsync(Guid subscriptionReference,
        RenewSubscriptionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TenantSubscriptionDto>();
        if (subscriptionReference == Guid.Empty || request == null ||
            request.ClientRequestId == Guid.Empty || request.NextPlanId is <= 0 ||
            !TryVersion(request.RowVersion, out var version))
            return Error<TenantSubscriptionDto>("Subscription reference, request ID and row version are required.");
        return await ScheduleRenewalCoreAsync(_user.TenantId, subscriptionReference,
            request.ClientRequestId, version, _user.UserId, false, ct, request.NextPlanId);
    }

    /// <summary>
    /// Trusted scheduler entry point, not an HTTP API. A system tenant scope must already
    /// be established by the background job. Shares the exact renewal transaction with
    /// the tenant-initiated workflow; never initiates or approves a payment.
    /// </summary>
    public async Task<ApiResponse<TenantSubscriptionDto>> ScheduleAutomaticRenewalAsync(
        long tenantId, long subscriptionId, CancellationToken ct = default)
    {
        if (tenantId <= 0 || subscriptionId <= 0)
            return Error<TenantSubscriptionDto>("Invalid automatic renewal target.");
        // Request users may never invoke a host-wide operation through this method.
        if (_user.IsAuthenticated && !_user.IsSuperAdmin)
            return Denied<TenantSubscriptionDto>();
        var row = await _subscriptions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == subscriptionId && x.TenantId == tenantId && !x.IsDeleted, ct);
        if (row == null) return Error<TenantSubscriptionDto>("Subscription not found.", 404);
        // Stable across retries and multiple servers. One predecessor => one successor.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:subscription:auto-renew:v1:{tenantId}:{subscriptionId}"));
        var requestId = new Guid(digest.AsSpan(0, 16));
        return await ScheduleRenewalCoreAsync(tenantId, row.PublicId, requestId,
            null, null, true, ct);
    }

    private async Task<ApiResponse<TenantSubscriptionDto>> ScheduleRenewalCoreAsync(
        long tenantId, Guid subscriptionReference, Guid requestId, byte[]? expectedVersion,
        long? actorId, bool automatic, CancellationToken ct, long? nextPlanId = null)
    {
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var previous = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.PublicId == subscriptionReference && !x.IsDeleted, token);
                if (previous == null) return Error<TenantSubscriptionDto>("Subscription not found.", 404);
                // Recheck the preference within the serializable transaction. Opt-out wins
                // if it commits before this job begins its renewal transaction.
                if (automatic && !previous.AutoRenew)
                    return Error<TenantSubscriptionDto>("Automatic renewal is disabled.", 409);
                var successor = await _subscriptions.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.PreviousSubscriptionId == previous.Id && !x.IsDeleted, token);
                if (successor != null)
                {
                    // Replaying the same idempotency key with a different target plan is a conflict.
                    // Automatic retries intentionally accept the already scheduled successor.
                    if (!automatic && (successor.PublicId != requestId ||
                        successor.SubscriptionPlanId != (nextPlanId ?? previous.SubscriptionPlanId)))
                        return Error<TenantSubscriptionDto>("A different renewal is already scheduled for this period.", 409);
                    return ApiResponse<TenantSubscriptionDto>.SuccessResponse(
                        await MapAsync(successor, token), "Renewal already scheduled.");
                }
                if (await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.PublicId == requestId && !x.IsDeleted, token))
                    return Error<TenantSubscriptionDto>("Request ID is already in use.", 409);
                if (!automatic && (expectedVersion == null ||
                    previous.RowVersion.Length != expectedVersion.Length ||
                    !CryptographicOperations.FixedTimeEquals(previous.RowVersion, expectedVersion)))
                    return Error<TenantSubscriptionDto>("Subscription changed. Reload and retry.", 409);

                var now = _clock.GetUtcNow().UtcDateTime;
                if (previous.State is not (SubscriptionState.Active or SubscriptionState.Trial or SubscriptionState.Grace) ||
                    previous.StartsAt > now || previous.EndsAt <= now ||
                    previous.EndsAt > now.AddDays(30) || previous.BillingCycleCode == "Lifetime" ||
                    !SubscriptionCalculator.IsSupportedCycle(previous.BillingCycleCode))
                    return Error<TenantSubscriptionDto>(
                        "Renewal is available during the final 30 days of an active, non-lifetime period.", 409);
                if (await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.Id != previous.Id && !x.IsDeleted &&
                    (x.State == SubscriptionState.PendingPayment ||
                     ((x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                       x.State == SubscriptionState.Grace) && x.EndsAt > now)), token))
                    return Error<TenantSubscriptionDto>("Another current or pending period already exists.", 409);

                var plan = await _plans.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == (nextPlanId ?? previous.SubscriptionPlanId) && x.IsActive && x.IsPublic && !x.IsDeleted, token);
                if (plan == null)
                    return Error<TenantSubscriptionDto>("The selected next-period plan is not available.", 409);
                // Currency changes require a separate, explicitly reviewed billing workflow.
                if (!string.Equals(plan.CurrencyCode, previous.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                    return Error<TenantSubscriptionDto>("The next-period plan must use the current billing currency.", 409);
                var price = SubscriptionCalculator.GetPriceForCycle(plan, previous.BillingCycleCode);
                if (price < 0m) return Error<TenantSubscriptionDto>("Invalid renewal price.", 409);
                var next = new TenantSubscription
                {
                    TenantId = tenantId, PublicId = requestId,
                    PreviousSubscriptionId = previous.Id, SubscriptionPlanId = plan.Id,
                    BillingCycleCode = previous.BillingCycleCode, IsTrial = false,
                    State = price == 0m ? SubscriptionState.Active : SubscriptionState.PendingPayment,
                    StartsAt = previous.EndsAt,
                    EndsAt = SubscriptionCalculator.CalculateEndDate(previous.EndsAt, previous.BillingCycleCode),
                    AutoRenew = previous.AutoRenew, PriceSnapshot = price, CurrencyCode = plan.CurrencyCode,
                    CreatedAt = now, CreatedBy = actorId
                };
                await _subscriptions.AddAsync(next);
                await _uow.SaveChangesAsync(token);
                if (price > 0m)
                {
                    var invoice = new SubscriptionInvoice
                    {
                        TenantId = tenantId, TenantSubscriptionId = next.Id,
                        InvoiceNumber = "REN-" + requestId.ToString("N"),
                        InvoiceDate = DateOnly.FromDateTime(now),
                        DueDate = DateOnly.FromDateTime(now.AddDays(7)),
                        Subtotal = price, TaxAmount = 0m, TotalAmount = price,
                        PaidAmount = 0m, DueAmount = price, CurrencyCode = plan.CurrencyCode,
                        State = InvoiceState.Issued, CreatedAt = now, CreatedBy = actorId
                    };
                    await _invoices.AddAsync(invoice);
                    await _uow.SaveChangesAsync(token);
                    await _invoiceLines.AddAsync(new SubscriptionInvoiceLine
                    {
                        TenantId = tenantId, SubscriptionInvoiceId = invoice.Id,
                        Description = "Renewal: " + plan.Name + " / " + next.BillingCycleCode,
                        Quantity = 1m, UnitPrice = price, Amount = price,
                        CreatedAt = now, CreatedBy = actorId
                    });
                    await _uow.SaveChangesAsync(token);
                }
                return ApiResponse<TenantSubscriptionDto>.SuccessResponse(await MapAsync(next, token),
                    price == 0m ? "Free renewal scheduled." :
                    "Renewal invoice issued. The next period activates only after verified payment.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<TenantSubscriptionDto>("Subscription changed concurrently. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting renewal for tenant {TenantId}", tenantId);
            return Error<TenantSubscriptionDto>("A concurrent renewal already exists. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Renewal scheduling failed for tenant {TenantId}", tenantId);
            return Error<TenantSubscriptionDto>("Renewal could not be scheduled.", 500);
        }
    }

}
