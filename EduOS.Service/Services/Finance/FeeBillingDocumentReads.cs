using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    // Documents are re-read from the authoritative ledger, never accepted from browser state.
    // Both the student and the document must belong to the authenticated tenant.
    public async Task<ApiResponse<StudentInvoiceDto>> GetStudentInvoiceDocumentAsync(
        Guid studentReference, Guid documentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<StudentInvoiceDto>("Finance document access required.", 403);
        if (studentReference == Guid.Empty || documentReference == Guid.Empty)
            return Error<StudentInvoiceDto>("Valid student and invoice references required.", 400);
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<StudentInvoiceDto>("Invoice not found.", 404);

        var enrollmentIds = _enrollments.GetQueryable().AsNoTracking().Where(e =>
            e.TenantId == _user.TenantId && e.StudentId == student.Id && !e.IsDeleted)
            .Select(e => e.Id);
        var id = await _invoices.GetQueryable().AsNoTracking()
            .Where(i => i.TenantId == _user.TenantId && !i.IsDeleted &&
                i.PublicId == documentReference && enrollmentIds.Contains(i.StudentEnrollmentId))
            .Select(i => (long?)i.Id).FirstOrDefaultAsync(ct);
        if (!id.HasValue) return Error<StudentInvoiceDto>("Invoice not found.", 404);
        var documents = await InvoiceDtosAsync(new[] { id.Value }, ct);
        if (documents.Count != 1) return Error<StudentInvoiceDto>("Invoice not found.", 404);
        return ApiResponse<StudentInvoiceDto>.SuccessResponse(documents[0]);
    }

    public async Task<ApiResponse<StudentPaymentDocumentDto>> GetStudentPaymentDocumentAsync(
        Guid studentReference, Guid documentReference, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<StudentPaymentDocumentDto>("Finance document access required.", 403);
        if (studentReference == Guid.Empty || documentReference == Guid.Empty)
            return Error<StudentPaymentDocumentDto>("Valid student and receipt references required.", 400);
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Error<StudentPaymentDocumentDto>("Receipt not found.", 404);
        var payment = await _payments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(p =>
            p.TenantId == _user.TenantId && !p.IsDeleted && p.StudentId == student.Id &&
            p.PublicId == documentReference, ct);
        if (payment == null) return Error<StudentPaymentDocumentDto>("Receipt not found.", 404);
        var documents = await PaymentDtosAsync(new[] { payment }, ct);
        if (documents.Count != 1) return Error<StudentPaymentDocumentDto>("Receipt not found.", 404);
        return ApiResponse<StudentPaymentDocumentDto>.SuccessResponse(new StudentPaymentDocumentDto
        {
            StudentReference = student.PublicId,
            StudentCode = student.StudentCode,
            StudentName = student.FullName,
            Payment = documents[0]
        });
    }
}
