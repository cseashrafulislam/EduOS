using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Tenant-wide fee discount rule administration. The canonical StudentDiscount assignments
/// consume these rules during future invoice generation; posted invoices are never rewritten.
/// </summary>
public sealed partial class FeeBillingService
{
    public async Task<ApiResponse<PagedResult<DiscountRuleDto>>> GetDiscountRulesAsync(
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!CanManageDiscountRules())
            return Error<PagedResult<DiscountRuleDto>>("Tenant administrator access required.", 403);
        if (!ValidPage(page, pageSize))
            return Error<PagedResult<DiscountRuleDto>>("Page size must be 1–100.");
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<DiscountRuleDto>>("Page exceeds limit.");
        var tenant = _user.TenantId;
        var query = _discountRules.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var rules = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        var headIds = rules.Where(x => x.FeeHeadId.HasValue)
            .Select(x => x.FeeHeadId!.Value).Distinct().ToArray();
        var heads = await _heads.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && headIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var items = rules.Select(x => MapDiscountRule(x,
            x.FeeHeadId.HasValue && heads.TryGetValue(x.FeeHeadId.Value, out var name) ? name : null)).ToArray();
        return ApiResponse<PagedResult<DiscountRuleDto>>.SuccessResponse(
            new PagedResult<DiscountRuleDto>
            { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
    }

    public Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(
        long? discountRuleId, SaveDiscountRuleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDiscountRules())
            return Task.FromResult(Error<DiscountRuleDto>("Tenant administrator access required.", 403));
        if (discountRuleId is <= 0 || request == null ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            request.FeeHeadId is <= 0 ||
            request.Value <= 0m || request.Value > 999999999999m ||
            request.Value != decimal.Round(request.Value, 4) ||
            request.IsPercentage && request.Value > 100m ||
            request.EffectiveFrom.HasValue && request.EffectiveTo.HasValue &&
            request.EffectiveFrom > request.EffectiveTo)
            return Task.FromResult(Error<DiscountRuleDto>(
                "Enter a name, a positive four-decimal value (percentage at most 100), and valid effective dates."));
        return SaveDiscountRuleCoreAsync(discountRuleId, request, ct);
    }

    private async Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleCoreAsync(
        long? id, SaveDiscountRuleRequestDto request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var rules = _discountRules.GetQueryable();
                var existingName = await rules.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Name.ToUpper() == name.ToUpper(), token);
                if (!id.HasValue && existingName != null)
                {
                    if (SameDiscountRule(existingName, request, name))
                        return ApiResponse<DiscountRuleDto>.SuccessResponse(
                            await MapDiscountRuleAsync(existingName, token), "Existing matching rule returned.");
                    return Error<DiscountRuleDto>("Discount rule name already exists with different terms.", 409);
                }
                var row = id.HasValue ? await rules.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == id.Value && !x.IsDeleted, token) : null;
                if (id.HasValue && row == null) return Error<DiscountRuleDto>("Discount rule not found.", 404);
                if (row != null && !Match(row.RowVersion, request.RowVersion))
                {
                    if (SameDiscountRule(row, request, name))
                        return ApiResponse<DiscountRuleDto>.SuccessResponse(
                            await MapDiscountRuleAsync(row, token), "Matching rule already saved.");
                    return Error<DiscountRuleDto>("Discount rule changed. Reload before editing.", 409);
                }
                if (existingName != null && existingName.Id != row?.Id)
                    return Error<DiscountRuleDto>("Discount rule name already exists.", 409);
                if (request.FeeHeadId.HasValue &&
                    !await _heads.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == request.FeeHeadId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return Error<DiscountRuleDto>("Active fee head not found in this institution.", 409);
                if (row != null && !SameFinancialTerms(row, request, name) &&
                    await _discounts.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.DiscountRuleId == row.Id, token))
                    return Error<DiscountRuleDto>(
                        "Assigned discount terms are immutable. Create a new rule for future assignments.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new DiscountRule { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                    await _discountRules.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now;
                    row.UpdatedBy = _user.UserId;
                }
                row.Name = name;
                row.FeeHeadId = request.FeeHeadId;
                row.IsPercentage = request.IsPercentage;
                row.Value = request.Value;
                row.EffectiveFrom = request.EffectiveFrom;
                row.EffectiveTo = request.EffectiveTo;
                row.IsActive = request.IsActive;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<DiscountRuleDto>.SuccessResponse(
                    await MapDiscountRuleAsync(row, token), "Discount rule saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error<DiscountRuleDto>("Discount rule changed. Reload before editing.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Discount rule conflict for tenant {TenantId}", tenant);
            return Error<DiscountRuleDto>("Conflicting discount rule operation.", 409);
        }
    }

    private bool CanManageDiscountRules() =>
        _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;

    private static bool SameFinancialTerms(
        DiscountRule rule, SaveDiscountRuleRequestDto request, string name) =>
        rule.Name == name && rule.FeeHeadId == request.FeeHeadId &&
        rule.IsPercentage == request.IsPercentage && rule.Value == request.Value &&
        rule.EffectiveFrom == request.EffectiveFrom && rule.EffectiveTo == request.EffectiveTo;

    private static bool SameDiscountRule(
        DiscountRule rule, SaveDiscountRuleRequestDto request, string name) =>
        SameFinancialTerms(rule, request, name) && rule.IsActive == request.IsActive;

    private async Task<DiscountRuleDto> MapDiscountRuleAsync(DiscountRule rule, CancellationToken ct)
    {
        string? headName = null;
        if (rule.FeeHeadId.HasValue)
            headName = await _heads.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _user.TenantId && x.Id == rule.FeeHeadId.Value && !x.IsDeleted)
                .Select(x => x.Name).FirstOrDefaultAsync(ct);
        return MapDiscountRule(rule, headName);
    }

    private static DiscountRuleDto MapDiscountRule(DiscountRule row, string? headName) => new()
    {
        Id = row.Id, Name = row.Name, FeeHeadId = row.FeeHeadId, FeeHeadName = headName,
        IsPercentage = row.IsPercentage, Value = row.Value,
        EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
}
