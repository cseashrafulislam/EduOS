using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    /// <summary>
    /// Cohort-wide invoice completeness audit. Unlike the 250-row generation preflight,
    /// this uses SQL aggregation and server paging for a full current academic batch.
    /// It never generates obligations or changes accounting records.
    /// </summary>
    public async Task<ApiResponse<FeeInvoiceCoverageReportDto>> GetInvoiceCoverageAsync(
        FeeInvoiceCoverageQueryDto request, CancellationToken ct = default)
    {
        if (!CanManage() || !_user.IsAuthenticated || _user.TenantId <= 0 ||
            _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<FeeInvoiceCoverageReportDto>("Finance access required.", 403);
        if (request == null || request.AcademicBatchId <= 0 ||
            request.BillingMonth.Year is < 2000 or > 2199 || request.BillingMonth.Day != 1 ||
            request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Search?.Trim().Length > 100)
            return Error<FeeInvoiceCoverageReportDto>("Choose a batch, first day of month and page size 1–100.");
        var status = (request.Status ?? "all").Trim().ToLowerInvariant();
        if (status is not ("all" or "billed" or "unbilled" or "review"))
            return Error<FeeInvoiceCoverageReportDto>("Invalid coverage status.");
        var skip = ((long)request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue)
            return Error<FeeInvoiceCoverageReportDto>("Page exceeds supported range.");
        var tenant = _user.TenantId;
        var month = request.BillingMonth;
        var monthEnd = month.AddMonths(1);
        var batch = await _batches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == request.AcademicBatchId &&
                x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name, x.CampusId, x.AcademicYearId,
                x.AcademicProgramId, x.AcademicLevelId }).FirstOrDefaultAsync(ct);
        if (batch == null || !await CanAccessBillingCampusAsync(batch.CampusId, ct))
            return Error<FeeInvoiceCoverageReportDto>("Academic batch unavailable.", 404);
        var campusName = await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == batch.CampusId &&
                x.IsActive && !x.IsDeleted).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var year = await _years.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == batch.AcademicYearId &&
                x.IsActive && !x.IsDeleted)
            .Select(x => new { x.StartDate, x.EndDate, x.CampusId }).FirstOrDefaultAsync(ct);
        if (campusName == null || year == null ||
            (year.CampusId.HasValue && year.CampusId != batch.CampusId) ||
            month < year.StartDate || monthEnd.AddDays(-1) > year.EndDate)
            return Error<FeeInvoiceCoverageReportDto>("Billing month is outside the active campus academic year.", 409);

        var eligible =
            from e in _enrollments.GetQueryable().AsNoTracking()
            join s in _students.GetQueryable().AsNoTracking()
                on new { e.TenantId, Id = e.StudentId } equals new { s.TenantId, s.Id }
            where e.TenantId == tenant && !e.IsDeleted && !s.IsDeleted &&
                e.AcademicBatchId == batch.Id && e.CampusId == batch.CampusId &&
                e.AcademicYearId == batch.AcademicYearId &&
                e.AcademicProgramId == batch.AcademicProgramId &&
                e.AcademicLevelId == batch.AcademicLevelId &&
                e.IsCurrent && e.State == EnrollmentState.Active && s.StatusCode == "Active"
            select new { EnrollmentId = e.Id, s.PublicId, s.StudentCode, s.FullName };
        // Correlated indexed counts avoid nullable left-join materialization across
        // EF InMemory and SQL Server. Tenant/enrolment predicates are server-side.
        var monthlyInvoices = _invoices.GetQueryable().AsNoTracking()
            .Where(i => i.TenantId == tenant && !i.IsDeleted &&
                i.InvoiceDate >= month && i.InvoiceDate < monthEnd &&
                i.State != InvoiceState.Cancelled);
        var coverage = eligible.Select(e => new
        {
            e.PublicId, e.StudentCode, e.FullName,
            InvoiceCount = monthlyInvoices.Count(i => i.StudentEnrollmentId == e.EnrollmentId),
            IntegrityIssueCount = monthlyInvoices.Count(i => i.StudentEnrollmentId == e.EnrollmentId &&
                (i.TotalAmount < 0m || i.PaidAmount < 0m || i.DueAmount < 0m ||
                 i.TotalAmount != i.PaidAmount + i.DueAmount ||
                 i.CurrencyCode == null || i.CurrencyCode == ""))
        });
        // Aggregate the entire authorized cohort, not just the current page or search.
        var summary = await coverage.GroupBy(x => 1).Select(g => new
        {
            Eligible = g.Count(),
            Unbilled = g.Count(x => x.InvoiceCount == 0),
            Review = g.Count(x => x.InvoiceCount > 1 || x.IntegrityIssueCount > 0)
        }).FirstOrDefaultAsync(ct);
        var filtered = status switch
        {
            "billed" => coverage.Where(x => x.InvoiceCount == 1 && x.IntegrityIssueCount == 0),
            "unbilled" => coverage.Where(x => x.InvoiceCount == 0),
            "review" => coverage.Where(x => x.InvoiceCount > 1 || x.IntegrityIssueCount > 0),
            _ => coverage
        };
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            filtered = filtered.Where(x => x.StudentCode.StartsWith(term) || x.FullName.StartsWith(term));
        var total = await filtered.CountAsync(ct);
        var rows = await filtered.OrderBy(x => x.StudentCode).ThenBy(x => x.PublicId)
            .Skip((int)skip).Take(request.PageSize)
            .Select(x => new FeeInvoiceCoverageRowDto
            {
                StudentReference = x.PublicId, StudentCode = x.StudentCode, StudentName = x.FullName,
                InvoiceCount = x.InvoiceCount, IntegrityIssueCount = x.IntegrityIssueCount,
                Status = x.InvoiceCount == 0 ? "Unbilled" :
                    x.InvoiceCount > 1 || x.IntegrityIssueCount > 0 ? "Review" : "Billed"
            }).ToListAsync(ct);
        return ApiResponse<FeeInvoiceCoverageReportDto>.SuccessResponse(
            new FeeInvoiceCoverageReportDto
            {
                AcademicBatchId = batch.Id, AcademicBatchName = batch.Name,
                CampusName = campusName, BillingMonth = month,
                Eligible = summary?.Eligible ?? 0, Unbilled = summary?.Unbilled ?? 0,
                Review = summary?.Review ?? 0,
                Billed = (summary?.Eligible ?? 0) - (summary?.Unbilled ?? 0) - (summary?.Review ?? 0),
                Students = new PagedResult<FeeInvoiceCoverageRowDto>
                {
                    Page = request.Page, PageSize = request.PageSize, TotalCount = total, Items = rows
                }
            });
    }
}
