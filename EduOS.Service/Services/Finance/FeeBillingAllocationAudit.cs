using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Tenant-wide, read-only financial evidence. Refunds can explain gross/net differences;
    // do not treat this as bank clearance, refund approval or verified GL posting.
    public async Task<ApiResponse<InvoiceAllocationAuditDto>> GetInvoiceAllocationAuditAsync(
        Guid studentReference, Guid invoiceReference, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Error<InvoiceAllocationAuditDto>("Tenant administrator access required.", 403);
        if (studentReference == Guid.Empty || invoiceReference == Guid.Empty ||
            page < 1 || page > 10000 || pageSize is < 1 or > 100)
            return Error<InvoiceAllocationAuditDto>("Valid student, invoice and page required.", 400);
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<InvoiceAllocationAuditDto>("Invoice not found.", 404);
        var tenant = _user.TenantId;
        var invoice = await (from i in _invoices.GetQueryable().AsNoTracking()
            join e in _enrollments.GetQueryable().AsNoTracking()
                on new { i.TenantId, Id = i.StudentEnrollmentId }
                equals new { e.TenantId, e.Id }
            where i.TenantId == tenant && !i.IsDeleted && !e.IsDeleted &&
                e.StudentId == student.Id && i.PublicId == invoiceReference
            select new { i.Id, i.InvoiceNumber, i.CurrencyCode, i.TotalAmount,
                i.PaidAmount, i.DueAmount }).FirstOrDefaultAsync(ct);
        if (invoice == null) return Error<InvoiceAllocationAuditDto>("Invoice not found.", 404);
        var query = from a in _allocations.GetQueryable().AsNoTracking()
            join p in _payments.GetQueryable().AsNoTracking()
                on new { a.TenantId, Id = a.StudentPaymentId }
                equals new { p.TenantId, p.Id }
            where a.TenantId == tenant && !a.IsDeleted && !p.IsDeleted &&
                a.StudentInvoiceId == invoice.Id && p.StudentId == student.Id
            select new { AllocationId = a.Id, a.Amount, PaymentId = p.Id,
                p.PublicId, p.ReceiptNumber, p.PaymentDate, p.State, p.JournalId };
        var count = await query.CountAsync(ct);
        var gross = await query.Where(x => x.State == PaymentState.Successful)
            .Select(x => (decimal?)x.Amount).SumAsync(ct) ?? 0m;
        var invalid = await query.AnyAsync(x =>
            x.Amount <= 0m || x.State != PaymentState.Successful, ct);
        var rows = await query.OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.PaymentId).ThenByDescending(x => x.AllocationId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InvoiceAllocationAuditRowDto
            {
                ReceiptReference = x.PublicId, ReceiptNumber = x.ReceiptNumber,
                PaymentDate = x.PaymentDate, PaymentState = x.State,
                AllocationAmount = x.Amount, JournalId = x.JournalId
            }).ToListAsync(ct);
        return ApiResponse<InvoiceAllocationAuditDto>.SuccessResponse(new InvoiceAllocationAuditDto
        {
            StudentReference = studentReference, InvoiceReference = invoiceReference,
            InvoiceNumber = invoice.InvoiceNumber, CurrencyCode = invoice.CurrencyCode,
            InvoiceTotal = invoice.TotalAmount, CurrentPaid = invoice.PaidAmount,
            CurrentDue = invoice.DueAmount, GrossSuccessfulAllocations = gross,
            GrossToNetDifference = gross - invoice.PaidAmount,
            MatchesCurrentInvoice = !invalid && gross == invoice.PaidAmount &&
                invoice.TotalAmount == invoice.PaidAmount + invoice.DueAmount,
            TotalAllocations = count, Page = page, PageSize = pageSize, Items = rows
        });
    }
}
