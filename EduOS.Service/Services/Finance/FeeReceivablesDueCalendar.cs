using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeReceivablesAgingService
{
    // No financial writes, implicit instalment creation, or cross-currency totals.
    // The report reflects today's remaining invoice balances, not historical balances.
    public async Task<ApiResponse<FeeDueCalendarReportDto>> GetDueCalendarAsync(
        FeeDueCalendarQueryDto request, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<FeeDueCalendarReportDto>.ErrorResponse("Institutional finance access denied.", 403);
        if (request == null || request.FromDate.Year is < 2000 or > 2200 ||
            request.ToDate.Year is < 2000 or > 2200 ||
            request.ToDate.DayNumber < request.FromDate.DayNumber ||
            request.ToDate.DayNumber - request.FromDate.DayNumber > 366 ||
            request.CampusId is <= 0 || request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Search?.Trim().Length > 100)
            return ApiResponse<FeeDueCalendarReportDto>.ErrorResponse(
                "Select a date range up to 366 days, an accessible campus and page size 1–100.");
        var skip = ((long)request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue)
            return ApiResponse<FeeDueCalendarReportDto>.ErrorResponse("Page exceeds supported range.");
        var tenant = _user.TenantId;
        var userId = _user.UserId;
        var campuses = _db.Campuses.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.IsActive && !c.IsDeleted);
        if (!_user.IsTenantAdmin)
            campuses = campuses.Where(c => _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == userId && a.CampusId == c.Id &&
                a.IsActive && !a.IsDeleted));
        if (request.CampusId.HasValue &&
            !await campuses.AnyAsync(c => c.Id == request.CampusId.Value, ct))
            return ApiResponse<FeeDueCalendarReportDto>.ErrorResponse("Campus not accessible.", 403);

        var campusOptions = await campuses.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Take(500).Select(c => new FeeReceivablesCampusDto
                { Id = c.Id, Name = c.Name }).ToListAsync(ct);
        var postedCredits = ReservationCreditNoteBalances.Posted(_db, tenant);
        var invoices =
            from i in _db.StudentInvoices.AsNoTracking()
            join e in _db.StudentEnrollments.AsNoTracking() on i.StudentEnrollmentId equals e.Id
            join s in _db.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in campuses on e.CampusId equals c.Id
            let applied = postedCredits.Where(x => x.InvoiceId == i.Id)
                .Select(x => (decimal?)x.UnpaidReduction).FirstOrDefault()
            let due = i.DueAmount - (applied ?? 0m)
            where i.TenantId == tenant && e.TenantId == tenant && s.TenantId == tenant &&
                !i.IsDeleted && !e.IsDeleted && !s.IsDeleted &&
                (i.State == InvoiceState.Issued || i.State == InvoiceState.PartiallyPaid) &&
                due > 0m && i.DueDate >= request.FromDate && i.DueDate <= request.ToDate
            select new
            {
                i.Id, i.PublicId, i.InvoiceNumber, i.DueDate, i.CurrencyCode,
                i.TotalAmount, i.PaidAmount, DueAmount = due,
                StudentReference = s.PublicId, s.StudentCode, StudentName = s.FullName,
                CampusId = c.Id, CampusName = c.Name
            };
        if (request.CampusId.HasValue)
            invoices = invoices.Where(x => x.CampusId == request.CampusId.Value);
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            invoices = invoices.Where(x => x.InvoiceNumber.StartsWith(term) ||
                x.StudentCode.StartsWith(term) || x.StudentName.StartsWith(term));

        // Fail closed instead of showing an apparently valid financial total from inconsistent rows.
        if (await invoices.AnyAsync(x => x.TotalAmount < 0m || x.PaidAmount < 0m ||
            x.DueAmount < 0m || x.DueAmount > x.TotalAmount - x.PaidAmount, ct))
            return ApiResponse<FeeDueCalendarReportDto>.ErrorResponse(
                "Invoice totals do not reconcile; review the affected financial records.", 409);

        var months = await invoices
            .GroupBy(x => new { Year = x.DueDate.Year, Month = x.DueDate.Month, x.CurrencyCode })
            .Select(g => new FeeDueCalendarMonthDto
            {
                Year = g.Key.Year, Month = g.Key.Month, CurrencyCode = g.Key.CurrencyCode,
                InvoiceCount = g.Count(), Outstanding = g.Sum(x => x.DueAmount)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.CurrencyCode)
            .ToListAsync(ct);
        var count = await invoices.CountAsync(ct);
        var rows = await invoices.OrderBy(x => x.DueDate).ThenBy(x => x.Id)
            .Skip((int)skip).Take(request.PageSize)
            .Select(x => new FeeDueCalendarInvoiceDto
            {
                InvoiceReference = x.PublicId, StudentReference = x.StudentReference,
                InvoiceNumber = x.InvoiceNumber, StudentCode = x.StudentCode,
                StudentName = x.StudentName, CampusName = x.CampusName,
                DueDate = x.DueDate, CurrencyCode = x.CurrencyCode,
                TotalAmount = x.TotalAmount, PaidAmount = x.PaidAmount, DueAmount = x.DueAmount
            }).ToListAsync(ct);
        return ApiResponse<FeeDueCalendarReportDto>.SuccessResponse(new FeeDueCalendarReportDto
        {
            FromDate = request.FromDate, ToDate = request.ToDate,
            Campuses = campusOptions, Months = months,
            Invoices = new PagedResult<FeeDueCalendarInvoiceDto>
            {
                Items = rows, TotalCount = count, Page = request.Page, PageSize = request.PageSize
            }
        });
    }
}
