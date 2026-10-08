using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Service.Helpers.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduOS.Service.Services.SaaS;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ISubscriptionInvoiceRepository _invoices;
    private readonly IGenericRepository<SubscriptionInvoiceLine> _invoiceLines;
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ManualPaymentSettings _manual;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(ITenantSubscriptionRepository subscriptions, ISubscriptionPlanRepository plans,
        ISubscriptionInvoiceRepository invoices, IGenericRepository<SubscriptionInvoiceLine> invoiceLines,
        IGenericRepository<Tenant> tenants, IGenericRepository<Student> students,
        IGenericRepository<Employee> employees, IGenericRepository<Campus> campuses,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IOptions<ManualPaymentSettings> manual, ILogger<SubscriptionService> logger)
    {
        _subscriptions = subscriptions; _plans = plans; _invoices = invoices; _invoiceLines = invoiceLines;
        _tenants = tenants; _students = students; _employees = employees; _campuses = campuses;
        _uow = unitOfWork; _user = currentUser; _manual = manual.Value; _logger = logger;
    }

    public async Task<ApiResponse<CreateSubscriptionResponseDto>> CreateAsync(CreateSubscriptionRequestDto request)
    {
        if (!CanManage()) return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || request.SubscriptionPlanId <= 0 || !Enum.IsDefined(request.BillingCycle))
            return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Plan and billing cycle are required.");
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
            return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Coupons are not supported.");
        var tenantId = _user.TenantId;
        try
        {
            var strategy = _uow.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await _uow.BeginTransactionAsync();
                var open = true;
                try
                {
                    var tenant = await _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == tenantId && x.IsActive);
                    if (tenant == null) return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Institution not found.", 404);
                    if (!tenant.IsEmailVerified)
                        return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Verify institution email before subscribing.", 409);
                    var plan = await _plans.GetQueryable().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == request.SubscriptionPlanId && x.IsActive && x.IsPublic);
                    if (plan == null) return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Plan not available.", 404);
                    var history = await _subscriptions.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenantId).Select(x => new { x.Id, x.State, x.EndsAt, x.IsTrial })
                        .OrderByDescending(x => x.Id).Take(100).ToListAsync();
                    if (history.Any(x => x.State == SubscriptionState.Suspended ||
                        ((x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                          x.State == SubscriptionState.Grace) && x.EndsAt > DateTime.UtcNow)))
                        return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("A current or pending subscription already exists.", 409);

                    var now = DateTime.UtcNow;
                    var trial = plan.TrialDays > 0 && !history.Any(x => x.IsTrial);
                    var amount = trial ? 0m : SubscriptionCalculator.GetPriceForCycle(plan, request.BillingCycle);
                    if (amount < 0m) return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Plan price is invalid.", 409);
                    var end = trial ? now.AddDays(plan.TrialDays) : SubscriptionCalculator.CalculateEndDate(now, request.BillingCycle);
                    var state = trial ? SubscriptionState.Trial : amount == 0m ? SubscriptionState.Active : SubscriptionState.Suspended;
                    var row = new TenantSubscription
                    {
                        TenantId = tenantId, SubscriptionPlanId = plan.Id,
                        BillingCycleCode = request.BillingCycle.ToString(), State = state,
                        IsTrial = trial, StartsAt = now, EndsAt = end,
                        AutoRenew = request.AutoRenew, PriceSnapshot = amount,
                        CurrencyCode = plan.CurrencyCode, CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _subscriptions.AddAsync(row);
                    await _uow.SaveChangesAsync();

                    SubscriptionInvoice? invoice = null;
                    if (amount > 0m)
                    {
                        invoice = new SubscriptionInvoice
                        {
                            TenantId = tenantId, TenantSubscriptionId = row.Id,
                            InvoiceNumber = await _invoices.GenerateNextInvoiceNumberAsync(),
                            InvoiceDate = DateOnly.FromDateTime(now), DueDate = DateOnly.FromDateTime(now.AddDays(7)),
                            Subtotal = amount, TaxAmount = 0m, TotalAmount = amount,
                            PaidAmount = 0m, DueAmount = amount, CurrencyCode = plan.CurrencyCode,
                            State = InvoiceState.Issued, CreatedAt = now, CreatedBy = _user.UserId
                        };
                        await _invoices.AddAsync(invoice);
                        await _uow.SaveChangesAsync();
                        await _invoiceLines.AddAsync(new SubscriptionInvoiceLine
                        {
                            TenantId = tenantId, SubscriptionInvoiceId = invoice.Id,
                            Description = plan.Name + " / " + request.BillingCycle,
                            Quantity = 1m, UnitPrice = amount, Amount = amount,
                            CreatedAt = now, CreatedBy = _user.UserId
                        });
                        await _uow.SaveChangesAsync();
                    }
                    if (tenant.OnboardingStage == OnboardingStage.PlanSelection)
                        tenant.OnboardingStage = state == SubscriptionState.Suspended
                            ? OnboardingStage.Payment : OnboardingStage.CampusSetup;
                    tenant.UpdatedAt = now; tenant.UpdatedBy = _user.UserId;
                    await _uow.SaveChangesAsync();
                    await _uow.CommitTransactionAsync(); open = false;
                    var result = new CreateSubscriptionResponseDto
                    {
                        SubscriptionId = row.Id, InvoiceId = invoice?.Id,
                        InvoiceNumber = invoice?.InvoiceNumber, Amount = amount,
                        Currency = row.CurrencyCode, Status = LegacyState(row),
                        IsTrialActivated = trial, TrialEndsAt = trial ? end : null,
                        Message = trial ? "Trial subscription started." : amount == 0m
                            ? "Free subscription activated." : "Invoice created. Payment is required for activation."
                    };
                    if (invoice != null)
                        result.ManualPaymentInstructions = new ManualPaymentInstructionsDto
                        {
                            BankName = _manual.BankName, AccountName = _manual.AccountName,
                            AccountNumber = _manual.AccountNumber, BranchName = _manual.BranchName,
                            RoutingNumber = _manual.RoutingNumber,
                            Reference = invoice.InvoiceNumber, Instructions = _manual.Instructions
                        };
                    return ApiResponse<CreateSubscriptionResponseDto>.SuccessResponse(result, result.Message);
                }
                finally
                {
                    if (open) await _uow.RollbackTransactionAsync();
                }
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Subscription changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Subscription conflict for tenant {TenantId}", tenantId);
            return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Subscription already exists or conflicts with another request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription creation failed for tenant {TenantId}", tenantId);
            return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse("Could not create subscription.", 500);
        }
    }

    public async Task<ApiResponse<CurrentSubscriptionDto>> GetCurrentAsync()
    {
        if (!CanRead()) return ApiResponse<CurrentSubscriptionDto>.ErrorResponse("Tenant access is required.", 403);
        var tenantId = _user.TenantId;
        var row = await _subscriptions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && (x.State == SubscriptionState.Trial ||
                x.State == SubscriptionState.Active || x.State == SubscriptionState.Grace ||
                x.State == SubscriptionState.Suspended))
            .OrderByDescending(x => x.StartsAt).FirstOrDefaultAsync();
        if (row == null) return ApiResponse<CurrentSubscriptionDto>.ErrorResponse("Subscription not found.", 404);
        var plan = await _plans.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.SubscriptionPlanId);
        var now = DateTime.UtcNow;
        var activeStudents = await _students.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.IsActive);
        var activeTeachers = await _employees.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.CanTeach && x.IsActive);
        var campuses = await _campuses.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.IsActive);
        var effectiveState = row.EndsAt <= now ? SubscriptionStatus.Expired : LegacyState(row);
        return ApiResponse<CurrentSubscriptionDto>.SuccessResponse(new CurrentSubscriptionDto
        {
            Id = row.Id, PlanId = row.SubscriptionPlanId, PlanName = plan?.Name ?? string.Empty,
            PlanCode = plan?.Code ?? string.Empty,
            BillingCycle = ParseBillingCycle(row.BillingCycleCode), Status = effectiveState,
            StartDate = row.StartsAt, EndDate = row.EndsAt,
            NextBillingDate = row.AutoRenew ? row.EndsAt : null,
            IsTrial = row.IsTrial, TrialEndDate = row.IsTrial ? row.EndsAt : null,
            TrialDaysRemaining = row.IsTrial ? Math.Max(0, (int)Math.Ceiling((row.EndsAt - now).TotalDays)) : null,
            Price = row.PriceSnapshot, FinalAmount = row.PriceSnapshot, Currency = row.CurrencyCode,
            AutoRenew = row.AutoRenew, CancelAtPeriodEnd = !row.AutoRenew &&
                row.State is SubscriptionState.Active or SubscriptionState.Trial,
            MaxStudents = plan?.MaxStudents ?? 0, CurrentStudents = activeStudents,
            MaxTeachers = plan?.MaxEmployees ?? 0, CurrentTeachers = activeTeachers,
            MaxCampuses = plan?.MaxCampuses ?? 0, CurrentCampuses = campuses,
            DaysRemaining = Math.Max(0, (int)Math.Ceiling((row.EndsAt - now).TotalDays))
        });
    }

    public async Task<ApiResponse<List<SubscriptionHistoryDto>>> GetHistoryAsync()
    {
        if (!CanRead()) return ApiResponse<List<SubscriptionHistoryDto>>.ErrorResponse("Tenant access is required.", 403);
        var tenantId = _user.TenantId;
        var rows = await _subscriptions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId).OrderByDescending(x => x.StartsAt).Take(200).ToListAsync();
        var ids = rows.Select(x => x.SubscriptionPlanId).Distinct().ToArray();
        var plans = await _plans.GetQueryable().AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);
        return ApiResponse<List<SubscriptionHistoryDto>>.SuccessResponse(rows.Select(x =>
            new SubscriptionHistoryDto
            {
                Id = x.Id, PlanName = plans.GetValueOrDefault(x.SubscriptionPlanId) ?? string.Empty,
                BillingCycle = ParseBillingCycle(x.BillingCycleCode), Status = LegacyState(x),
                StartDate = x.StartsAt, EndDate = x.EndsAt,
                FinalAmount = x.PriceSnapshot, Currency = x.CurrencyCode
            }).ToList());
    }

    public async Task<ApiResponse<bool>> CancelAsync(long subscriptionId, string? reason, bool cancelAtPeriodEnd = true)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Tenant administrator access is required.", 403);
        if (!string.IsNullOrWhiteSpace(reason))
            return ApiResponse<bool>.ErrorResponse("Cancellation reasons are not persisted by the current subscription model.");
        var tenantId = _user.TenantId;
        try
        {
            var row = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == subscriptionId);
            if (row == null) return ApiResponse<bool>.ErrorResponse("Subscription not found.", 404);
            if (row.State is SubscriptionState.Expired or SubscriptionState.Cancelled)
                return ApiResponse<bool>.ErrorResponse("Subscription has already ended.", 409);
            row.AutoRenew = false;
            if (!cancelAtPeriodEnd)
            {
                row.State = SubscriptionState.Cancelled;
                row.CancelledAt = DateTime.UtcNow;
                row.EndsAt = row.CancelledAt.Value;
            }
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true,
                cancelAtPeriodEnd ? "Auto-renew disabled; subscription continues until period end." : "Subscription cancelled.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Subscription changed. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription cancellation failed for tenant {TenantId}", tenantId);
            return ApiResponse<bool>.ErrorResponse("Cancellation failed.", 500);
        }
    }

    public async Task<ApiResponse<bool>> ToggleAutoRenewAsync(long subscriptionId, bool autoRenew)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Tenant administrator access is required.", 403);
        var tenantId = _user.TenantId;
        try
        {
            var row = await _subscriptions.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == subscriptionId);
            if (row == null) return ApiResponse<bool>.ErrorResponse("Subscription not found.", 404);
            if (row.State is SubscriptionState.Cancelled or SubscriptionState.Expired or SubscriptionState.Suspended)
                return ApiResponse<bool>.ErrorResponse("Auto-renew cannot be changed for this subscription.", 409);
            row.AutoRenew = autoRenew; row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, autoRenew ? "Auto-renew enabled." : "Auto-renew disabled.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.ErrorResponse("Subscription changed. Reload and retry.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-renew update failed for tenant {TenantId}", tenantId);
            return ApiResponse<bool>.ErrorResponse("Could not update auto-renew.", 500);
        }
    }

    public async Task<ApiResponse<bool>> ActivateAfterPaymentAsync(long subscriptionId, long tenantId)
    {
        if (subscriptionId <= 0 || tenantId <= 0) return ApiResponse<bool>.ErrorResponse("Subscription reference is invalid.");
        try
        {
            var row = await _subscriptions.GetByIdForSystemAsync(subscriptionId, tenantId);
            if (row == null) return ApiResponse<bool>.ErrorResponse("Subscription not found.", 404);
            if (row.State == SubscriptionState.Active) return ApiResponse<bool>.SuccessResponse(true, "Subscription already active.");
            if (row.State is SubscriptionState.Cancelled or SubscriptionState.Expired)
                return ApiResponse<bool>.ErrorResponse("Ended subscription cannot be activated.", 409);
            var invoices = await _invoices.GetByTenantAsync(tenantId);
            var settled = invoices.Any(x => x.TenantSubscriptionId == subscriptionId &&
                x.State == InvoiceState.Paid && x.PaidAmount == x.TotalAmount && x.DueAmount == 0m);
            if (!settled) return ApiResponse<bool>.ErrorResponse("Payment has not been fully settled.", 409);
            row.State = SubscriptionState.Active;
            row.IsTrial = false;
            row.UpdatedAt = DateTime.UtcNow;
            _subscriptions.Update(row);
            await _uow.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Subscription activated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Subscription changed while payment was being posted.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Activation failure for tenant {TenantId} subscription {Id}", tenantId, subscriptionId);
            return ApiResponse<bool>.ErrorResponse("Subscription could not be activated.", 500);
        }
    }

    public async Task<ApiResponse<bool>> CheckExpiryAsync(long tenantId)
    {
        if (tenantId <= 0) return ApiResponse<bool>.ErrorResponse("Tenant reference is invalid.");
        try
        {
            var row = await _subscriptions.GetActiveByTenantAsync(tenantId);
            if (row == null || row.EndsAt > DateTime.UtcNow) return ApiResponse<bool>.SuccessResponse(true);
            row.State = SubscriptionState.Expired;
            row.UpdatedAt = DateTime.UtcNow;
            _subscriptions.Update(row);
            await _uow.SaveChangesAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Subscription expired.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.SuccessResponse(true, "Subscription changed concurrently."); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription expiry processing failed for tenant {TenantId}", tenantId);
            return ApiResponse<bool>.ErrorResponse("Expiry check failed.", 500);
        }
    }

    private static BillingCycle ParseBillingCycle(string value) =>
        Enum.TryParse<BillingCycle>(value, true, out var result) && Enum.IsDefined(result) ? result : BillingCycle.Monthly;
    private static SubscriptionStatus LegacyState(TenantSubscription x) => x.State switch
    {
        SubscriptionState.Trial => SubscriptionStatus.Trialing,
        SubscriptionState.Active => !x.AutoRenew ? SubscriptionStatus.CancelAtPeriodEnd : SubscriptionStatus.Active,
        SubscriptionState.Grace => SubscriptionStatus.PastDue,
        SubscriptionState.Suspended => SubscriptionStatus.PendingPayment,
        SubscriptionState.Cancelled => SubscriptionStatus.Cancelled,
        SubscriptionState.Expired => SubscriptionStatus.Expired,
        _ => SubscriptionStatus.PendingPayment
    };
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
}
