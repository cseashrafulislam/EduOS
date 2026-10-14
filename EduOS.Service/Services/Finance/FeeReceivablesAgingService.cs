using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Live operational receivables aging. Never mixes platform subscription invoices or implies
/// historical GL balances. All joins explicitly enforce tenant and campus visibility.
/// </summary>
public sealed class FeeReceivablesAgingService : IFeeReceivablesAgingService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    public FeeReceivablesAgingService(EduOSDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    private bool Allowed => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Accountant"));

    public async Task<ApiResponse<FeeReceivablesAgingReportDto>> GetReportAsync(
        FeeReceivablesAgingQueryDto request, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<FeeReceivablesAgingReportDto>.ErrorResponse("Institutional finance access denied.", 403);
        if (request == null || request.AgingDate.Year is < 2000 or > 2200 ||
            request.CampusId is <= 0 || request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Search?.Trim().Length > 100)
            return ApiResponse<FeeReceivablesAgingReportDto>.ErrorResponse(
                "Valid aging date, campus, search and page size (1–100) are required.");
        var tenant = _user.TenantId;
        var userId = _user.UserId;
        var campuses = _db.Campuses.AsNoTracking()
            .Where(c => c.TenantId == tenant && !c.IsDeleted);
        if (!_user.IsTenantAdmin)
            campuses = campuses.Where(c => _db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && !a.IsDeleted && a.IsActive &&
                a.UserId == userId && a.CampusId == c.Id));
        if (request.CampusId.HasValue &&
            !await campuses.AnyAsync(c => c.Id == request.CampusId.Value, ct))
            return ApiResponse<FeeReceivablesAgingReportDto>.ErrorResponse("Campus not accessible.", 403);
        var campusOptions = await campuses.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new FeeReceivablesCampusDto { Id = c.Id, Name = c.Name })
            .Take(500).ToListAsync(ct);

        var invoices =
            from i in _db.StudentInvoices.AsNoTracking()
            join e in _db.StudentEnrollments.AsNoTracking() on i.StudentEnrollmentId equals e.Id
            join s in _db.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in campuses on e.CampusId equals c.Id
            where i.TenantId == tenant && e.TenantId == tenant && s.TenantId == tenant &&
                !i.IsDeleted && !e.IsDeleted && !s.IsDeleted &&
                (i.State == InvoiceState.Issued || i.State == InvoiceState.PartiallyPaid) &&
                i.DueAmount > 0m
            select new
            {
                i.Id, i.PublicId, i.InvoiceNumber, i.InvoiceDate, i.DueDate,
                i.CurrencyCode, i.TotalAmount, i.PaidAmount, i.DueAmount,
                StudentReference = s.PublicId, s.StudentCode, StudentName = s.FullName,
                CampusId = c.Id, CampusName = c.Name
            };
        if (request.CampusId.HasValue)
            invoices = invoices.Where(x => x.CampusId == request.CampusId.Value);
        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            invoices = invoices.Where(x => x.InvoiceNumber.StartsWith(term) ||
                x.StudentCode.StartsWith(term) || x.StudentName.StartsWith(term));

        var agingDate = request.AgingDate;
        var d30 = agingDate.AddDays(-30);
        var d60 = agingDate.AddDays(-60);
        var d90 = agingDate.AddDays(-90);
        var totals = await invoices.GroupBy(x => x.CurrencyCode)
            .Select(g => new FeeReceivablesCurrencyDto
            {
                CurrencyCode = g.Key,
                InvoiceCount = g.Count(),
                Outstanding = g.Sum(x => x.DueAmount),
                Current = g.Sum(x => x.DueDate >= agingDate ? x.DueAmount : 0m),
                Days1To30 = g.Sum(x => x.DueDate < agingDate && x.DueDate >= d30 ? x.DueAmount : 0m),
                Days31To60 = g.Sum(x => x.DueDate < d30 && x.DueDate >= d60 ? x.DueAmount : 0m),
                Days61To90 = g.Sum(x => x.DueDate < d60 && x.DueDate >= d90 ? x.DueAmount : 0m),
                DaysOver90 = g.Sum(x => x.DueDate < d90 ? x.DueAmount : 0m)
            }).OrderBy(x => x.CurrencyCode).ToListAsync(ct);
        if (totals.Any(x => x.Outstanding != x.Current + x.Days1To30 +
            x.Days31To60 + x.Days61To90 + x.DaysOver90))
            return ApiResponse<FeeReceivablesAgingReportDto>.ErrorResponse(
                "Aging totals do not reconcile.", 409);

        var count = await invoices.CountAsync(ct);
        var offset = ((long)request.Page - 1) * request.PageSize;
        var pageRows = offset > int.MaxValue ? null : await invoices
            .OrderBy(x => x.DueDate).ThenBy(x => x.Id)
            .Skip((int)offset).Take(request.PageSize).ToListAsync(ct);
        var rows = (pageRows ?? []).Select(x =>
        {
            var days = Math.Max(0, agingDate.DayNumber - x.DueDate.DayNumber);
            return new FeeReceivablesInvoiceDto
            {
                Reference = x.PublicId, StudentReference = x.StudentReference,
                InvoiceNumber = x.InvoiceNumber, StudentCode = x.StudentCode,
                DisplayName = x.StudentName, CampusName = x.CampusName,
                InvoiceDate = x.InvoiceDate, DueDate = x.DueDate,
                CurrencyCode = x.CurrencyCode, TotalAmount = x.TotalAmount,
                PaidAmount = x.PaidAmount, DueAmount = x.DueAmount,
                DaysOverdue = days,
                AgingBucket = days == 0 ? "Current" : days <= 30 ? "1–30" :
                    days <= 60 ? "31–60" : days <= 90 ? "61–90" : "90+"
            };
        }).ToList();
        return ApiResponse<FeeReceivablesAgingReportDto>.SuccessResponse(new FeeReceivablesAgingReportDto
        {
            AgingDate = agingDate, Campuses = campusOptions, Totals = totals,
            Invoices = new PagedResult<FeeReceivablesInvoiceDto>
            {
                Items = rows, TotalCount = count, Page = request.Page, PageSize = request.PageSize
            }
        });
    }
}
