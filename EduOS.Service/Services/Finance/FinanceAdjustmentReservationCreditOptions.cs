using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>Review-only, tenant/campus/enrollment-bound invoice and account picker.</summary>
    public async Task<ApiResponse<ReservationCreditOptionsDto>> GetReservationCreditOptionsAsync(
        string kind, long clearanceId, string? invoiceSearch, string? accountSearch,
        CancellationToken ct = default)
    {
        if (!Authorized) return Error<ReservationCreditOptionsDto>("Finance permission required.", 403);
        if (kind is not ("hostel" or "transport") || clearanceId <= 0 ||
            invoiceSearch?.Length > 100 || accountSearch?.Length > 100)
            return Error<ReservationCreditOptionsDto>("Invalid clearance or search.");
        var tenant = user.TenantId;
        long enrollmentId, campusId;
        if (kind == "hostel")
        {
            var row = await db.HostelFinanceClearances.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == clearanceId && !x.IsDeleted &&
                x.State == HostelClearanceState.Pending &&
                x.Operation == HostelClearanceOperation.UpcomingStartReschedule &&
                x.RequestedByUserId != user.UserId, ct);
            if (row == null) return Error<ReservationCreditOptionsDto>("Clearance unavailable.", 404);
            enrollmentId = row.StudentEnrollmentId;
            campusId = row.CampusId;
        }
        else
        {
            var row = await db.TransportFinanceClearances.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == clearanceId && !x.IsDeleted &&
                x.State == TransportClearanceState.Pending &&
                x.Operation == TransportClearanceOperation.UpcomingRiderExtension &&
                x.RequestedByUserId != user.UserId, ct);
            if (row == null) return Error<ReservationCreditOptionsDto>("Clearance unavailable.", 404);
            enrollmentId = row.StudentEnrollmentId;
            campusId = row.CampusId;
        }
        if (!await db.StudentEnrollments.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == enrollmentId && x.CampusId == campusId &&
            x.IsCurrent && !x.IsDeleted && x.State == EnrollmentState.Active, ct))
            return Error<ReservationCreditOptionsDto>("Enrollment changed; refresh clearance.", 409);

        var invoices = from invoice in db.StudentInvoices.AsNoTracking()
            join journal in db.Journals.AsNoTracking()
                on new { invoice.TenantId, Id = invoice.JournalId }
                equals new { journal.TenantId, Id = (long?)journal.Id }
            where invoice.TenantId == tenant && invoice.StudentEnrollmentId == enrollmentId &&
                !invoice.IsDeleted && journal.State == JournalState.Posted &&
                journal.SourceType == "StudentInvoice" && journal.SourceId == invoice.Id &&
                journal.CampusId == campusId && invoice.CurrencyCode == "BDT" &&
                (invoice.State == InvoiceState.Issued || invoice.State == InvoiceState.PartiallyPaid ||
                 invoice.State == InvoiceState.Paid) &&
                !db.Journals.IgnoreQueryFilters().Any(x => x.TenantId == tenant &&
                    x.ReversalOfJournalId == journal.Id && x.State == JournalState.Posted)
            select invoice;
        var invoiceTerm = invoiceSearch?.Trim();
        if (!string.IsNullOrEmpty(invoiceTerm))
            invoices = invoices.Where(x => x.InvoiceNumber.StartsWith(invoiceTerm));
        var invoiceRows = await invoices.OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.Id).Take(51)
            .Select(x => new ReservationCreditSourceDto
            {
                InvoiceReference = x.PublicId, InvoiceNumber = x.InvoiceNumber,
                InvoiceDate = x.InvoiceDate, OriginalTotal = x.TotalAmount,
                GrossPaid = x.PaidAmount, GrossDue = x.DueAmount
            }).ToListAsync(ct);
        var accounts = db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
            x.CurrencyCode == "BDT" && x.AccountType == AccountType.Liability);
        var accountTerm = accountSearch?.Trim();
        if (!string.IsNullOrEmpty(accountTerm))
            accounts = accounts.Where(x => x.Code.StartsWith(accountTerm) || x.Name.StartsWith(accountTerm));
        var accountRows = await accounts.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Take(51).Select(x => new ReservationCreditAccountDto
            { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync(ct);
        return ApiResponse<ReservationCreditOptionsDto>.SuccessResponse(new ReservationCreditOptionsDto
        {
            Invoices = invoiceRows.Take(50).ToArray(), LiabilityAccounts = accountRows.Take(50).ToArray(),
            MoreInvoices = invoiceRows.Count > 50, MoreAccounts = accountRows.Count > 50
        });
    }
}
