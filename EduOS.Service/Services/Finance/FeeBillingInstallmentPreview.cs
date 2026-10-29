using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Same effective award selection as actual invoice issuance. This preview is
    // not a reservation: issuance rechecks all state under its serializable write.
    private async Task<List<DiscountRule>> GetActiveInstallmentDiscountRulesAsync(
        EduOSDbContext db, StudentFeeInstallmentPlan plan, DateOnly month, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        return await (from award in db.StudentDiscounts.AsNoTracking()
            join rule in db.DiscountRules.AsNoTracking()
                on new { award.TenantId, Id = award.DiscountRuleId }
                equals new { rule.TenantId, rule.Id }
            where award.TenantId == tenant && award.StudentEnrollmentId == plan.StudentEnrollmentId &&
                award.IsActive && !award.IsDeleted && rule.IsActive && !rule.IsDeleted &&
                award.EffectiveFrom <= month &&
                (!award.EffectiveTo.HasValue || award.EffectiveTo >= month) &&
                (!rule.EffectiveFrom.HasValue || rule.EffectiveFrom <= month) &&
                (!rule.EffectiveTo.HasValue || rule.EffectiveTo >= month)
            select rule).ToListAsync(ct);
    }

    public async Task<ApiResponse<StudentFeeInstallmentMonthPreviewDto>> PreviewInstallmentMonthAsync(
        Guid reference, IssueStudentFeeInstallmentMonthDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Finance access required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.MonthStart.Year is < 2000 or > 2200 || request.MonthStart.Day != 1 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 128)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Invalid plan, month or version.");
        var tenant = _user.TenantId;
        var plan = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
        if (plan == null || !await CanAccessBillingCampusAsync(plan.CampusId, ct))
            return Error<StudentFeeInstallmentMonthPreviewDto>("Plan unavailable.", 404);
        if (Convert.ToBase64String(plan.RowVersion) != request.RowVersion)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Plan changed; refresh.", 409);
        if (request.MonthStart < plan.StartMonth || request.MonthStart > plan.EndMonth)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Month is outside this agreement.", 409);
        var actions = await db.StudentFeeInstallmentPlanEvents.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
            .OrderBy(x => x.ActionCode).Take(4).Select(x => x.ActionCode).ToListAsync(ct);
        if (actions.Count != 2 || actions[0] != 2 || actions[1] != 3)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Independent approval is required.", 409);
        if (!await db.StudentEnrollments.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == plan.StudentEnrollmentId &&
            !x.IsDeleted && x.IsCurrent && x.State == EnrollmentState.Active &&
            x.CampusId == plan.CampusId && x.AcademicYearId == plan.AcademicYearId, ct))
            return Error<StudentFeeInstallmentMonthPreviewDto>("Enrollment changed; preview unavailable.", 409);
        if (await InstallmentPlanDtoAsync(db, plan, true, ct) == null)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Frozen agreement integrity failed.", 409);
        var lines = await db.StudentFeeInstallmentPlanLines.AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id &&
                !x.IsDeleted && x.MonthStart == request.MonthStart)
            .OrderBy(x => x.FeeHeadId).Take(51).ToListAsync(ct);
        if (lines.Count is < 1 or > 50 || lines.Any(x =>
            x.GrossAmount <= 0m || x.GrossAmount != decimal.Round(x.GrossAmount, 2) ||
            x.DueDate < request.MonthStart || x.DueDate >= request.MonthStart.AddMonths(1)) ||
            lines.Select(x => x.FeeHeadId).Distinct().Count() != lines.Count)
            return Error<StudentFeeInstallmentMonthPreviewDto>("Frozen month lines are invalid.", 409);
        var key = InstallmentMonthKey(plan.PublicId, request.MonthStart);
        if (await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted &&
            ((x.StudentEnrollmentId == plan.StudentEnrollmentId &&
                x.InvoiceDate >= request.MonthStart &&
                x.InvoiceDate < request.MonthStart.AddMonths(1)) ||
                x.ClientRequestId == key), ct))
            return Error<StudentFeeInstallmentMonthPreviewDto>(
                "Invoice evidence already exists for this month; review it instead.", 409);
        var awards = await GetActiveInstallmentDiscountRulesAsync(db, plan, request.MonthStart, ct);
        var discounts = CalculateLineDiscounts(lines.Select(x =>
            (HeadId: x.FeeHeadId, Amount: x.GrossAmount)).ToArray(), awards);
        var gross = lines.Sum(x => x.GrossAmount);
        var discount = discounts.Sum();
        return ApiResponse<StudentFeeInstallmentMonthPreviewDto>.SuccessResponse(
            new StudentFeeInstallmentMonthPreviewDto {
                PlanReference = plan.PublicId, MonthStart = request.MonthStart,
                DueDate = lines.Max(x => x.DueDate), CurrencyCode = plan.CurrencyCode,
                GrossAmount = gross, DiscountAmount = discount, NetAmount = gross - discount,
                Lines = lines.Select((line, i) => new StudentFeeInstallmentMonthPreviewLineDto {
                    FeeHeadId = line.FeeHeadId, FeeHeadCode = line.FeeHeadCodeSnapshot,
                    FeeHeadName = line.FeeHeadNameSnapshot, DueDate = line.DueDate,
                    GrossAmount = line.GrossAmount, DiscountAmount = discounts[i],
                    NetAmount = line.GrossAmount - discounts[i]
                }).ToArray()
            }, "Read-only estimate; issuance revalidates the agreement and current awards.");
    }
}
