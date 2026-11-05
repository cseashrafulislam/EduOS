using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingSetupService
{
    private IQueryable<CashFlowActivityRule> FlowRules => db.CashFlowActivityRules
        .Where(x => x.TenantId == user.TenantId && !x.IsDeleted);

    private async Task<bool> CashFlowActor(bool reviewer, CancellationToken ct)
    {
        if (!TenantMatched || !(reviewer ? user.IsTenantAdmin : Writable) || user.UserId <= 0)
            return false;
        return await db.Users.AsNoTracking().AnyAsync(x => x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct) &&
            await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.UserId == user.UserId && !x.IsDeleted && x.Status == MembershipStatus.Active, ct) &&
            await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId &&
                db.Roles.IgnoreQueryFilters().Any(r => r.Id == ur.RoleId && r.IsActive && !r.IsDeleted &&
                    (r.TenantId == user.TenantId || r.TenantId == null && r.IsSystemRole) &&
                    (r.NormalizedName == "TENANTADMIN" || !reviewer && r.NormalizedName == "PRINCIPAL")), ct);
    }

    private static CashFlowRuleDto FlowMap(CashFlowActivityRule x, Account account) => new()
    {
        Id=x.Id, AccountId=x.AccountId, AccountCode=account.Code, AccountName=account.Name,
        EffectiveFromDate=x.EffectiveFromDate, ActivityCode=x.ActivityCode, StateCode=x.StateCode,
        Reason=x.Reason, SubmittedByUserId=x.SubmittedByUserId, SubmittedAtUtc=x.SubmittedAtUtc,
        ReviewedByUserId=x.ReviewedByUserId, ReviewedAtUtc=x.ReviewedAtUtc,
        ReviewReason=x.ReviewReason, RowVersion=V(x.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<CashFlowRuleDto>>> GetCashFlowRulesAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        if (!await CashFlowActor(false, ct)) return Denied<PagedResult<CashFlowRuleDto>>();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
            return ApiResponse<PagedResult<CashFlowRuleDto>>.ErrorResponse("Valid page and size required.", 400);
        var q = from rule in FlowRules.AsNoTracking()
            join account in Accounts.AsNoTracking() on rule.AccountId equals account.Id
            select new { Rule = rule, Account = account };
        var count = await q.CountAsync(ct);
        var records = await q.OrderByDescending(x => x.Rule.EffectiveFromDate)
            .ThenByDescending(x => x.Rule.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<CashFlowRuleDto>>.SuccessResponse(new()
        {
            Page=page, PageSize=pageSize, TotalCount=count,
            Items=records.Select(x => FlowMap(x.Rule, x.Account)).ToList()
        });
    }

    public async Task<ApiResponse<CashFlowRuleDto>> SubmitCashFlowRuleAsync(
        SubmitCashFlowRuleDto request, CancellationToken ct = default)
    {
        if (!await CashFlowActor(false, ct)) return Denied<CashFlowRuleDto>();
        if (request == null || request.AccountId <= 0 || request.ClientRequestId == Guid.Empty ||
            request.EffectiveFromDate.Year is < 2000 or > 2100 ||
            request.ActivityCode is not ("Operating" or "Investing" or "Financing") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 20 or > 500)
            return ApiResponse<CashFlowRuleDto>.ErrorResponse("Valid posting account, activity, effective date and 20–500 character reason required.", 400);
        var reason = request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var old = await FlowRules.FirstOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
            if (old != null)
            {
                if (old.SubmittedByUserId != user.UserId || old.AccountId != request.AccountId ||
                    old.EffectiveFromDate != request.EffectiveFromDate || old.ActivityCode != request.ActivityCode ||
                    old.Reason != reason)
                    return ApiResponse<CashFlowRuleDto>.ErrorResponse("Request reference reused with different classification.", 409);
                var originalAccount = await Accounts.AsNoTracking().FirstAsync(x => x.Id == old.AccountId, ct);
                return ApiResponse<CashFlowRuleDto>.SuccessResponse(FlowMap(old, originalAccount));
            }
            var account = await Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AccountId, ct);
            if (account == null || !account.IsActive || account.IsGroup || !account.AllowsPosting ||
                account.CurrencyCode != "BDT" || account.IsCashInHand ||
                await Banks.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.AccountId == request.AccountId, ct))
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("Select a non-cash/non-bank BDT posting account.", 409);
            if (await FlowRules.AnyAsync(x => x.AccountId == request.AccountId &&
                x.EffectiveFromDate == request.EffectiveFromDate &&
                (x.StateCode == "Submitted" || x.StateCode == "Approved"), ct))
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("A pending or approved policy already covers this account and date.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var rule = new CashFlowActivityRule { TenantId=user.TenantId,
                AccountId=request.AccountId, EffectiveFromDate=request.EffectiveFromDate,
                ActivityCode=request.ActivityCode, ClientRequestId=request.ClientRequestId,
                Reason=reason, SubmittedByUserId=user.UserId, SubmittedAtUtc=now,
                CreatedAt=now, CreatedBy=user.UserId };
            db.CashFlowActivityRules.Add(rule);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashFlowRuleDto>.SuccessResponse(FlowMap(rule, account));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashFlowRuleDto>.ErrorResponse("Classification changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashFlowRuleDto>.ErrorResponse("Concurrent classification conflict; reload.", 409); }
    }

    public async Task<ApiResponse<CashFlowRuleDto>> ReviewCashFlowRuleAsync(
        long id, ReviewCashFlowRuleDto request, CancellationToken ct = default)
    {
        if (!await CashFlowActor(true, ct)) return Denied<CashFlowRuleDto>();
        if (id <= 0 || request == null || request.ReviewRequestId == Guid.Empty ||
            request.Reason?.Length > 500 || !request.Approve && string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<CashFlowRuleDto>.ErrorResponse("Review reference and rejection reason required.", 400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var rule = await FlowRules.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (rule == null) return ApiResponse<CashFlowRuleDto>.ErrorResponse("Classification request not found.", 404);
            var account = await Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rule.AccountId, ct);
            if (account == null) return ApiResponse<CashFlowRuleDto>.ErrorResponse("Referenced account missing.", 409);
            if (rule.ReviewRequestId == request.ReviewRequestId &&
                rule.StateCode == (request.Approve ? "Approved" : "Rejected") &&
                rule.ReviewedByUserId == user.UserId && rule.ReviewReason == reason)
                return ApiResponse<CashFlowRuleDto>.SuccessResponse(FlowMap(rule, account));
            if (rule.StateCode != "Submitted")
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("Rule already reviewed; approved history is immutable.", 409);
            if (rule.SubmittedByUserId == user.UserId)
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("Maker cannot review their own cash-flow policy.", 403);
            if (!Version(rule.RowVersion, request.RowVersion))
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("Rule changed; reload.", 409);
            if (request.Approve && (!account.IsActive || account.IsGroup || !account.AllowsPosting ||
                account.IsCashInHand || account.CurrencyCode != "BDT" ||
                await Banks.AsNoTracking().AnyAsync(x => !x.IsDeleted && x.AccountId == account.Id, ct)))
                return ApiResponse<CashFlowRuleDto>.ErrorResponse("Counterparty account configuration changed; reject and resubmit.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            rule.StateCode = request.Approve ? "Approved" : "Rejected";
            rule.ReviewRequestId = request.ReviewRequestId;
            rule.ReviewedByUserId = user.UserId; rule.ReviewedAtUtc = now;
            rule.ReviewReason = reason; rule.UpdatedAt = now; rule.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<CashFlowRuleDto>.SuccessResponse(FlowMap(rule, account));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CashFlowRuleDto>.ErrorResponse("Review changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CashFlowRuleDto>.ErrorResponse("Concurrent review conflict; reload.", 409); }
    }
}
