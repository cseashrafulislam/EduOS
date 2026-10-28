using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    /// <summary>
    /// Bounded forward-looking billing planning, not a posted instalment plan.
    /// Uses the current active cohort and effective-dated monthly fee lines only.
    /// </summary>
    public async Task<ApiResponse<FeeInstallmentProjectionReportDto>> GetInstallmentProjectionAsync(
        FeeInstallmentProjectionQueryDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db ||
            db.CurrentTenantId != _user.TenantId || !_user.IsAuthenticated || _user.TenantId <= 0)
            return Error<FeeInstallmentProjectionReportDto>("Finance access required.", 403);
        if (request == null || request.AcademicBatchId <= 0 || request.FeeStructureId <= 0 ||
            request.StartMonth.Year is < 2000 or > 2199 || request.StartMonth.Day != 1 ||
            request.Months is < 1 or > 12)
            return Error<FeeInstallmentProjectionReportDto>("Choose a batch, fee structure, first day of month and 1–12 months.");
        var tenant = _user.TenantId;
        var start = request.StartMonth;
        var exclusiveEnd = start.AddMonths(request.Months);
        var lastMonth = exclusiveEnd.AddMonths(-1);

        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
        var structure = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.FeeStructureId && x.IsActive && !x.IsDeleted, ct);
        if (batch == null || structure == null ||
            batch.CampusId != structure.CampusId || batch.AcademicYearId != structure.AcademicYearId ||
            (structure.AcademicBatchId.HasValue && structure.AcademicBatchId != batch.Id) ||
            (structure.AcademicLevelId.HasValue && structure.AcademicLevelId != batch.AcademicLevelId) ||
            (structure.AcademicProgramId.HasValue && structure.AcademicProgramId != batch.AcademicProgramId) ||
            structure.EffectiveFrom > start ||
            (structure.EffectiveTo.HasValue && structure.EffectiveTo.Value < lastMonth) ||
            structure.CurrencyCode != "BDT")
            return Error<FeeInstallmentProjectionReportDto>("Structure is not effective for the entire selected batch period.", 409);
        if (!await CanAccessBillingCampusAsync(batch.CampusId, ct))
            return Error<FeeInstallmentProjectionReportDto>("Campus unavailable.", 404);
        var campus = await _campuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.Id == batch.CampusId && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name }).FirstOrDefaultAsync(ct);
        var year = await _years.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.StartDate, x.EndDate, x.CampusId }).FirstOrDefaultAsync(ct);
        if (campus == null || year == null ||
            (year.CampusId.HasValue && year.CampusId != campus.Id) ||
            start < year.StartDate || lastMonth > year.EndDate)
            return Error<FeeInstallmentProjectionReportDto>("Period is outside the active campus academic year.", 409);

        var lines = await (from line in _structureLines.GetQueryable().AsNoTracking()
            join head in _heads.GetQueryable().AsNoTracking()
                on new { line.TenantId, Id = line.FeeHeadId }
                equals new { head.TenantId, head.Id }
            where line.TenantId == tenant && line.FeeStructureId == structure.Id &&
                !line.IsDeleted && !head.IsDeleted && head.IsActive
            select new { line.FeeHeadId, line.Amount, line.Frequency, line.DueDayOfMonth })
            .Take(101).ToListAsync(ct);
        if (lines.Count is < 1 or > 100 ||
            lines.Any(x => x.Frequency != FeeFrequencyType.Monthly || x.Amount < 0m ||
                x.Amount != decimal.Round(x.Amount, 2) ||
                x.DueDayOfMonth is < 1 or > 31) ||
            lines.GroupBy(x => x.FeeHeadId).Any(g => g.Count() > 1))
            return Error<FeeInstallmentProjectionReportDto>("Only valid unique monthly fee heads can be projected.", 409);
        var perStudent = lines.Sum(x => x.Amount);
        if (perStudent <= 0m)
            return Error<FeeInstallmentProjectionReportDto>("Monthly gross fee must be positive.", 409);

        // Tenant/campus/academic membership is enforced at SQL level, without materializing students.
        var currentEnrollmentCount = await (from e in _enrollments.GetQueryable().AsNoTracking()
            join s in _students.GetQueryable().AsNoTracking()
                on new { e.TenantId, Id = e.StudentId } equals new { s.TenantId, s.Id }
            where e.TenantId == tenant && e.AcademicBatchId == batch.Id &&
                e.CampusId == batch.CampusId && e.AcademicYearId == batch.AcademicYearId &&
                e.AcademicLevelId == batch.AcademicLevelId &&
                e.AcademicProgramId == batch.AcademicProgramId &&
                e.IsCurrent && e.State == EnrollmentState.Active && !e.IsDeleted &&
                !s.IsDeleted && s.StatusCode == "Active"
            select e.Id).CountAsync(ct);
        var cohortGross = perStudent * currentEnrollmentCount;
        if (cohortGross > 9999999999999999.99m)
            return Error<FeeInstallmentProjectionReportDto>("Projected total exceeds supported financial range.", 409);

        // Observed issued invoices are separate facts. Never assume they were produced
        // from this structure, or subtract them from projected gross/discounted amounts.
        var issued = from i in _invoices.GetQueryable().AsNoTracking()
            join e in _enrollments.GetQueryable().AsNoTracking()
                on new { i.TenantId, Id = i.StudentEnrollmentId }
                equals new { e.TenantId, e.Id }
            where i.TenantId == tenant && !i.IsDeleted && !e.IsDeleted &&
                e.CampusId == batch.CampusId && e.AcademicBatchId == batch.Id &&
                e.AcademicYearId == batch.AcademicYearId &&
                i.InvoiceDate >= start && i.InvoiceDate < exclusiveEnd &&
                i.State != InvoiceState.Cancelled
            select new { i.InvoiceDate, i.TotalAmount, i.PaidAmount, i.DueAmount };
        if (await issued.AnyAsync(x => x.TotalAmount < 0m || x.PaidAmount < 0m ||
            x.DueAmount != x.TotalAmount - x.PaidAmount, ct))
            return Error<FeeInstallmentProjectionReportDto>("Existing invoice totals do not reconcile.", 409);
        var observed = await issued.GroupBy(x => new { x.InvoiceDate.Year, x.InvoiceDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count(),
                Total = g.Sum(x => x.TotalAmount) }).ToListAsync(ct);
        var byMonth = observed.ToDictionary(x => (x.Year, x.Month));
        var months = new List<FeeInstallmentProjectionMonthDto>(request.Months);
        for (var index = 0; index < request.Months; index++)
        {
            var month = start.AddMonths(index);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            var configuredDays = lines.Where(x => x.DueDayOfMonth.HasValue)
                .Select(x => Math.Min(x.DueDayOfMonth!.Value, lastDay)).ToArray();
            var hasUnspecified = lines.Any(x => !x.DueDayOfMonth.HasValue);
            byMonth.TryGetValue((month.Year, month.Month), out var billed);
            months.Add(new FeeInstallmentProjectionMonthDto
            {
                Year = month.Year, Month = month.Month, FeeHeadCount = lines.Count,
                GrossPerStudent = perStudent, GrossForCurrentCohort = cohortGross,
                HasUnspecifiedDueDays = hasUnspecified,
                EarliestConfiguredDueDate = configuredDays.Length == 0 ? null :
                    new DateOnly(month.Year, month.Month, configuredDays.Min()),
                LatestConfiguredDueDate = configuredDays.Length == 0 ? null :
                    new DateOnly(month.Year, month.Month, configuredDays.Max()),
                ObservedInvoiceCount = billed?.Count ?? 0,
                ObservedInvoiceTotal = billed?.Total ?? 0m
            });
        }
        return ApiResponse<FeeInstallmentProjectionReportDto>.SuccessResponse(
            new FeeInstallmentProjectionReportDto
            {
                CampusId = campus.Id, CampusName = campus.Name,
                AcademicBatchId = batch.Id, AcademicBatchName = batch.Name,
                FeeStructureId = structure.Id, FeeStructureName = structure.Name,
                CurrencyCode = structure.CurrencyCode,
                CurrentEligibleEnrollments = currentEnrollmentCount,
                Months = months
            });
    }
}
