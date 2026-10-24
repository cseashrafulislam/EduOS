using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class StudentPaymentPostingService
{
    public async Task<ApiResponse<StudentPaymentPostingRegisterDto>> GetRegisterAsync(
        long? campusId, string? search, bool unpostedOnly, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Error<StudentPaymentPostingRegisterDto>("Institutional finance access denied.", 403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (search?.Trim().Length ?? 0) > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<StudentPaymentPostingRegisterDto>("Invalid filters or pagination.");
        var tenant = user.TenantId;
        var wide = user.IsTenantAdmin || user.IsInRole("Principal");
        var allowed = AllowedCampuses;
        var campuses = await db.Campuses.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && (wide || allowed.Contains(x.Id)))
            .OrderBy(x => x.Name).Take(201)
            .Select(x => new StudentInvoicePostingCampusDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        if (campuses.Count > 200) return Error<StudentPaymentPostingRegisterDto>("Too many campuses.", 409);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new StudentInvoicePostingPeriodDto { Id = x.Id, Name = x.Name,
                StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (periods.Count > 100) return Error<StudentPaymentPostingRegisterDto>("Too many open periods.", 409);
        // A cash account must be an explicitly named institutional cash asset ledger, not a bank ledger.
        var cashAccounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && x.AccountType == AccountType.Asset &&
            (x.Code.ToUpper().Contains("CASH") || x.Name.ToUpper().Contains("CASH")) &&
            !db.BankAccounts.Any(b => b.TenantId == tenant && !b.IsDeleted && b.AccountId == x.Id))
            .OrderBy(x => x.Code).Take(101)
            .Select(x => new StudentPaymentPostingCashAccountDto { Id = x.Id, Code = x.Code, Name = x.Name })
            .ToListAsync(ct);
        if (cashAccounts.Count > 100) return Error<StudentPaymentPostingRegisterDto>("Too many cash ledgers.", 409);

        // Group by all allocations so a campus accountant cannot see mixed-campus receipts.
        var receiptCampus = from a in db.PaymentAllocations.AsNoTracking()
            join invoice in db.StudentInvoices.AsNoTracking()
                on new { a.TenantId, Id = a.StudentInvoiceId }
                equals new { invoice.TenantId, invoice.Id }
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { invoice.TenantId, Id = invoice.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            where a.TenantId == tenant && !a.IsDeleted && !invoice.IsDeleted && !enrollment.IsDeleted
            group enrollment.CampusId by a.StudentPaymentId into g
            select new { PaymentId = g.Key, CampusId = g.Min(), MaxCampusId = g.Max() };
        var query = from payment in db.StudentPayments.AsNoTracking()
            join student in db.Students.AsNoTracking()
                on new { payment.TenantId, Id = payment.StudentId }
                equals new { student.TenantId, student.Id }
            join c in receiptCampus on payment.Id equals c.PaymentId
            where payment.TenantId == tenant && !payment.IsDeleted && !student.IsDeleted &&
                c.CampusId == c.MaxCampusId && (wide || allowed.Contains(c.CampusId))
            select new { payment, student, c.CampusId };
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (unpostedOnly) query = query.Where(x => x.payment.JournalId == null);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term)) query = query.Where(x =>
            x.payment.ReceiptNumber.StartsWith(term) || x.student.StudentCode.StartsWith(term) ||
            x.student.FullName.StartsWith(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.payment.PaymentDate)
            .ThenByDescending(x => x.payment.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.payment.PublicId, x.payment.ReceiptNumber,
                x.student.StudentCode, x.student.FullName, x.CampusId,
                x.payment.PaymentDate, x.payment.Amount, x.payment.CurrencyCode,
                x.payment.JournalId, x.payment.RowVersion }).ToListAsync(ct);
        var ids = rows.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).Distinct().ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.State }).ToListAsync(ct);
        var byId = journals.ToDictionary(x => x.Id);
        return ApiResponse<StudentPaymentPostingRegisterDto>.SuccessResponse(new StudentPaymentPostingRegisterDto
        {
            OpenPeriods = periods, Campuses = campuses, CashAccounts = cashAccounts,
            Page = page, PageSize = pageSize, TotalCount = count,
            Payments = rows.Select(x => new StudentPaymentPostingRowDto
            {
                PaymentReference = x.PublicId, ReceiptNumber = x.ReceiptNumber,
                StudentCode = x.StudentCode, StudentName = x.FullName, CampusId = x.CampusId,
                PaymentDate = x.PaymentDate, Amount = x.Amount, CurrencyCode = x.CurrencyCode,
                Posted = x.JournalId.HasValue && byId.TryGetValue(x.JournalId.Value, out var posted) &&
                    posted.State == JournalState.Posted,
                JournalReference = x.JournalId.HasValue && byId.TryGetValue(x.JournalId.Value, out var journal)
                    ? journal.PublicId : null,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToArray()
        });
    }
}
