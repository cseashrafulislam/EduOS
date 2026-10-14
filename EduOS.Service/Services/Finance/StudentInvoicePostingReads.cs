using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Finance;
public sealed partial class StudentInvoicePostingService
{
    public async Task<ApiResponse<StudentInvoicePostingRegisterDto>> GetRegisterAsync(
        long? campusId, string? search, bool unpostedOnly, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Error<StudentInvoicePostingRegisterDto>("Institutional finance access denied.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (search?.Trim().Length ?? 0) > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<StudentInvoicePostingRegisterDto>("Invalid filters or pagination.");
        var tenant = user.TenantId;
        var wide = user.IsTenantAdmin || user.IsInRole("Principal");
        var allowed = AllowedCampuses;
        var campuses = await db.Campuses.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && (wide || allowed.Contains(x.Id)))
            .OrderBy(x => x.Name).Take(201).Select(x => new StudentInvoicePostingCampusDto
                { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        if (campuses.Count > 200) return Error<StudentInvoicePostingRegisterDto>("Too many campuses.", 409);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new StudentInvoicePostingPeriodDto { Id = x.Id, Name = x.Name,
                StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (periods.Count > 100) return Error<StudentInvoicePostingRegisterDto>("Too many open periods.", 409);
        var query = from invoice in db.StudentInvoices.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where invoice.TenantId == tenant && !invoice.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                (wide || allowed.Contains(enrollment.CampusId))
            select new { invoice, enrollment, student };
        if (campusId.HasValue) query = query.Where(x => x.enrollment.CampusId == campusId.Value);
        if (unpostedOnly) query = query.Where(x => x.invoice.JournalId == null);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term)) query = query.Where(x =>
            x.invoice.InvoiceNumber.StartsWith(term) || x.student.StudentCode.StartsWith(term) ||
            x.student.FullName.StartsWith(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.invoice.InvoiceDate).ThenByDescending(x => x.invoice.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.invoice.PublicId, x.invoice.InvoiceNumber,
                x.student.StudentCode, x.student.FullName, x.enrollment.CampusId,
                x.invoice.InvoiceDate, x.invoice.TotalAmount, x.invoice.CurrencyCode,
                x.invoice.JournalId, x.invoice.RowVersion }).ToListAsync(ct);
        var journalIds = rows.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).Distinct().ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && journalIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.State }).ToListAsync(ct);
        var byId = journals.ToDictionary(x => x.Id);
        return ApiResponse<StudentInvoicePostingRegisterDto>.SuccessResponse(new StudentInvoicePostingRegisterDto
        {
            OpenPeriods = periods, Campuses = campuses, Page = page, PageSize = pageSize, TotalCount = count,
            Invoices = rows.Select(x => new StudentInvoicePostingRowDto {
                InvoiceReference = x.PublicId, InvoiceNumber = x.InvoiceNumber,
                StudentCode = x.StudentCode, StudentName = x.FullName, CampusId = x.CampusId,
                InvoiceDate = x.InvoiceDate, TotalAmount = x.TotalAmount, CurrencyCode = x.CurrencyCode,
                Posted = x.JournalId.HasValue && byId.TryGetValue(x.JournalId.Value, out var posted) &&
                    posted.State == JournalState.Posted,
                JournalReference = x.JournalId.HasValue && byId.TryGetValue(x.JournalId.Value, out var journal)
                    ? journal.PublicId : null,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToArray()
        });
    }
}
