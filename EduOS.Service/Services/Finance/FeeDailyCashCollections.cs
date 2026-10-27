using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Tenant/campus-scoped gross cash receipt register. Includes only fully attributable,
/// intact receipt allocations; anomalies belong in the existing payment reconciliation.
/// Does not infer net cash after refunds or certify GL posting.
/// </summary>
public sealed partial class FeeReceivablesAgingService
{
    public async Task<ApiResponse<DailyCashCollectionReportDto>> GetDailyCashCollectionsAsync(
        DailyCashCollectionQueryDto request, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<DailyCashCollectionReportDto>.ErrorResponse(
                "Institutional finance access denied.", 403);
        if (request == null || request.FromDate.Year is < 2000 or > 2200 ||
            request.ToDate.Year > 2200 || request.ToDate < request.FromDate ||
            request.ToDate.DayNumber - request.FromDate.DayNumber > 30 ||
            request.CampusId is <= 0 || request.Page < 1 ||
            request.PageSize is < 1 or > 100 ||
            ((long)request.Page - 1) * request.PageSize > int.MaxValue)
            return ApiResponse<DailyCashCollectionReportDto>.ErrorResponse(
                "Choose a valid period of up to 31 days, accessible campus and page size 1–100.");

        var tenant = _user.TenantId;
        var campuses = _db.Campuses.AsNoTracking().Where(c =>
            c.TenantId == tenant && !c.IsDeleted);
        if (!_user.IsTenantAdmin)
            campuses = campuses.Where(c => _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == _user.UserId &&
                a.CampusId == c.Id && a.IsActive && !a.IsDeleted));
        if (request.CampusId.HasValue &&
            !await campuses.AnyAsync(c => c.Id == request.CampusId.Value, ct))
            return ApiResponse<DailyCashCollectionReportDto>.ErrorResponse(
                "Campus not accessible.", 403);
        var options = await campuses.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new DailyCashCollectionCampusDto { Id = c.Id, Name = c.Name })
            .Take(201).ToListAsync(ct);
        if (options.Count > 200)
            return ApiResponse<DailyCashCollectionReportDto>.ErrorResponse(
                "More than 200 accessible campuses; narrow the report scope.", 422);

        // Count every historical allocation, including tombstones, to ensure that
        // missing/deleted/foreign invoice evidence cannot disappear from a receipt.
        var allAllocations = _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenant)
            .GroupBy(a => a.StudentPaymentId)
            .Select(g => new { PaymentId = g.Key, Count = g.Count() });

        var validAllocationScopes =
            from a in _db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            join invoice in _db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
                on new { a.TenantId, Id = a.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            join enrollment in _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where a.TenantId == tenant && !a.IsDeleted && !invoice.IsDeleted &&
                !enrollment.IsDeleted && a.Amount > 0m
            group new { a.Amount, enrollment.CampusId } by a.StudentPaymentId into g
            select new
            {
                PaymentId = g.Key, CampusId = g.Min(x => x.CampusId),
                MaxCampusId = g.Max(x => x.CampusId),
                Allocated = g.Sum(x => x.Amount), Count = g.Count()
            };

        var eligible =
            from payment in _db.StudentPayments.AsNoTracking()
            join student in _db.Students.AsNoTracking()
                on new { payment.TenantId, Id = payment.StudentId }
                equals new { student.TenantId, student.Id }
            join scope in validAllocationScopes on payment.Id equals scope.PaymentId
            join all in allAllocations on payment.Id equals all.PaymentId
            join campus in campuses on new { payment.TenantId, Id = scope.CampusId }
                equals new { campus.TenantId, campus.Id }
            where payment.TenantId == tenant && !payment.IsDeleted && !student.IsDeleted &&
                payment.PaymentMethod == PaymentMethodType.Cash &&
                (payment.State == PaymentState.Successful || payment.State == PaymentState.Refunded) &&
                payment.PaymentDate >= request.FromDate && payment.PaymentDate <= request.ToDate &&
                payment.Amount > 0m && payment.CurrencyCode == "BDT" &&
                scope.CampusId == scope.MaxCampusId &&
                scope.Count == all.Count && scope.Allocated == payment.Amount
            select new { payment, student, campus };
        if (request.CampusId.HasValue)
            eligible = eligible.Where(x => x.campus.Id == request.CampusId.Value);

        // Aggregate in SQL over the entire filtered period, not over the current page.
        var summary = await eligible.GroupBy(x => new
            { x.campus.Id, x.campus.Name, x.payment.CurrencyCode })
            .Select(g => new DailyCashCollectionSummaryDto
            {
                CampusId = g.Key.Id, CampusName = g.Key.Name,
                CurrencyCode = g.Key.CurrencyCode,
                ReceiptCount = g.Count(), GrossCollected = g.Sum(x => x.payment.Amount)
            })
            .OrderBy(x => x.CampusName).ThenBy(x => x.CampusId)
            .Take(501).ToListAsync(ct);
        if (summary.Count > 500)
            return ApiResponse<DailyCashCollectionReportDto>.ErrorResponse(
                "Too many campus/currency summary groups.", 422);
        var count = await eligible.CountAsync(ct);
        var rows = await eligible.OrderByDescending(x => x.payment.PaymentDate)
            .ThenByDescending(x => x.payment.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new DailyCashCollectionRowDto
            {
                PaymentDate = x.payment.PaymentDate,
                ReceiptNumber = x.payment.ReceiptNumber,
                StudentCode = x.student.StudentCode,
                CampusName = x.campus.Name,
                ReceivedByUserId = x.payment.ReceivedByUserId,
                Amount = x.payment.Amount, CurrencyCode = x.payment.CurrencyCode,
                PaymentState = x.payment.State == PaymentState.Refunded ? "Refunded" : "Successful",
                HasJournalLink = x.payment.JournalId.HasValue
            }).ToListAsync(ct);
        return ApiResponse<DailyCashCollectionReportDto>.SuccessResponse(new DailyCashCollectionReportDto
        {
            FromDate = request.FromDate, ToDate = request.ToDate,
            Campuses = options, Summary = summary,
            Receipts = new PagedResult<DailyCashCollectionRowDto>
            {
                Page = request.Page, PageSize = request.PageSize,
                TotalCount = count, Items = rows
            }
        });
    }
}
