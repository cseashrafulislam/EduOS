using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Finance;
public sealed partial class StudentInvoicePostingService
{
    public async Task<ApiResponse<PostStudentInvoiceJournalResultDto>> PostAsync(
        Guid invoiceReference, PostStudentInvoiceJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PostStudentInvoiceJournalResultDto>("Institution administrator permission required.", 403);
        if (invoiceReference == Guid.Empty || request == null || request.AccountingPeriodId <= 0 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<PostStudentInvoiceJournalResultDto>("Invoice, period and concurrency token required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var source = await (from invoiceRow in db.StudentInvoices
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { invoiceRow.TenantId, Id = invoiceRow.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                join student in db.Students.AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.StudentId }
                    equals new { student.TenantId, student.Id }
                join campus in db.Campuses.AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.CampusId }
                    equals new { campus.TenantId, campus.Id }
                where invoiceRow.TenantId == tenant && invoiceRow.PublicId == invoiceReference &&
                    !invoiceRow.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted && !campus.IsDeleted
                select new { invoice = invoiceRow, enrollment.CampusId, enrollment.StudentId,
                    enrollment.AcademicProgramId }).SingleOrDefaultAsync(ct);
            if (source == null) return Error<PostStudentInvoiceJournalResultDto>("Invoice not found.", 404);
            var invoice = source.invoice;
            if (invoice.JournalId.HasValue)
            {
                var prior = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == invoice.JournalId.Value && !x.IsDeleted, ct);
                if (prior == null || prior.SourceType != "StudentInvoice" || prior.SourceId != invoice.Id ||
                    prior.State != JournalState.Posted)
                    return Error<PostStudentInvoiceJournalResultDto>("Linked journal requires reconciliation.", 409);
                return ApiResponse<PostStudentInvoiceJournalResultDto>.SuccessResponse(
                    new PostStudentInvoiceJournalResultDto { InvoiceReference = invoice.PublicId,
                        JournalReference = prior.PublicId, JournalNumber = prior.JournalNumber,
                        Amount = invoice.TotalAmount, AlreadyPosted = true }, "Already posted.");
            }
            if (!Version(invoice.RowVersion, request.RowVersion))
                return Error<PostStudentInvoiceJournalResultDto>("Invoice changed; reload.", 409);
            if (invoice.State is not (InvoiceState.Issued or InvoiceState.PartiallyPaid or InvoiceState.Paid) ||
                invoice.CurrencyCode != "BDT" || !Money(invoice.TotalAmount) || invoice.TotalAmount <= 0m ||
                !Money(invoice.PaidAmount) || !Money(invoice.DueAmount) ||
                invoice.PaidAmount + invoice.DueAmount != invoice.TotalAmount)
                return Error<PostStudentInvoiceJournalResultDto>("Invoice state or totals invalid.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId &&
                !x.IsDeleted && !x.IsClosed && x.StartDate <= invoice.InvoiceDate &&
                x.EndDate >= invoice.InvoiceDate, ct);
            if (period == null) return Error<PostStudentInvoiceJournalResultDto>("Open period must cover invoice date.", 409);
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                ((x.SourceType == "StudentInvoice" && x.SourceId == invoice.Id) ||
                x.IdempotencyKey == PostingKey(tenant, invoice.PublicId)), ct))
                return Error<PostStudentInvoiceJournalResultDto>("Unlinked source journal exists; reconcile first.", 409);
            var (error, entries) = await BuildEntriesAsync(invoice, source.CampusId,
                source.StudentId, source.AcademicProgramId, ct);
            if (error != null) return Error<PostStudentInvoiceJournalResultDto>(error, 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var journal = new Journal {
                TenantId = tenant, PublicId = Guid.NewGuid(),
                IdempotencyKey = PostingKey(tenant, invoice.PublicId),
                JournalNumber = "JRN-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                JournalDate = invoice.InvoiceDate, AccountingPeriodId = period.Id,
                CampusId = source.CampusId, ReferenceNumber = invoice.InvoiceNumber,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "StudentInvoice", SourceId = invoice.Id,
                Description = "Student fee invoice " + invoice.InvoiceNumber,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now, CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            foreach (var entry in entries)
            {
                entry.JournalId = journal.Id; entry.CreatedAt = now; entry.CreatedBy = user.UserId;
            }
            db.JournalLines.AddRange(entries);
            invoice.JournalId = journal.Id; invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostStudentInvoiceJournalResultDto>.SuccessResponse(
                new PostStudentInvoiceJournalResultDto { InvoiceReference = invoice.PublicId,
                    JournalReference = journal.PublicId, JournalNumber = journal.JournalNumber,
                    Amount = invoice.TotalAmount }, "Institutional invoice journal posted.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PostStudentInvoiceJournalResultDto>("Invoice changed during posting; reload.", 409); }
        catch (DbUpdateException)
        { return Error<PostStudentInvoiceJournalResultDto>("Posting conflict; retry after refresh.", 409); }
    }
}
