using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;
public sealed partial class FinanceAdjustmentService
{
    // Read-only tenant-owned award register, without global Person or accounting joins.
    public async Task<ApiResponse<DiscountAwardRegisterDto>> GetDiscountAwardRegisterAsync(
        long? campusId, long? ruleId, bool? active, string? search,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!Authorized) return Error<DiscountAwardRegisterDto>("Tenant administrator access required.", 403);
        if (campusId is <= 0 || ruleId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            search?.Trim().Length > 100 || ((long)page - 1) * pageSize > int.MaxValue)
            return Error<DiscountAwardRegisterDto>("Invalid filters or page.", 400);
        var tenant = user.TenantId;
        if (campusId.HasValue && !await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == campusId.Value, ct))
            return Error<DiscountAwardRegisterDto>("Campus not accessible.", 404);
        if (ruleId.HasValue && !await db.DiscountRules.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == ruleId.Value, ct))
            return Error<DiscountAwardRegisterDto>("Rule not accessible.", 404);

        var query =
            from award in db.StudentDiscounts.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { award.TenantId, Id = award.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            join campus in db.Campuses.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.CampusId }
                equals new { campus.TenantId, campus.Id }
            join rule in db.DiscountRules.AsNoTracking()
                on new { award.TenantId, Id = award.DiscountRuleId }
                equals new { rule.TenantId, rule.Id }
            where award.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && campus.TenantId == tenant && rule.TenantId == tenant &&
                !award.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                !campus.IsDeleted && !rule.IsDeleted
            select new DiscountAwardRowDto
            {
                Id = award.Id, EnrollmentReference = enrollment.PublicId,
                StudentCode = student.StudentCode, CampusId = campus.Id,
                CampusName = campus.Name, RuleId = rule.Id, RuleName = rule.Name,
                RuleValue = rule.Value, IsPercentage = rule.IsPercentage,
                EffectiveFrom = award.EffectiveFrom, EffectiveTo = award.EffectiveTo,
                IsActive = award.IsActive, ApprovedByUserId = award.ApprovedByUserId
            };
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (ruleId.HasValue) query = query.Where(x => x.RuleId == ruleId.Value);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
            query = query.Where(x => x.StudentCode.StartsWith(term) || x.RuleName.StartsWith(term));
        var total = await query.CountAsync(ct);
        var enabled = await query.CountAsync(x => x.IsActive, ct);
        var rows = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<DiscountAwardRegisterDto>.SuccessResponse(new DiscountAwardRegisterDto
        {
            TotalCount = total, ActiveCount = enabled, InactiveCount = total - enabled,
            Awards = new PagedResult<DiscountAwardRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total, Items = rows
            }
        });
    }
}
