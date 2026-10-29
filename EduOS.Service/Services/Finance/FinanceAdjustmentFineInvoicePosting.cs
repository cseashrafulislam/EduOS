using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Finance;

/// <summary>Post an assessed fixed fine as a separate, traceable invoice and balanced journal.</summary>
public sealed partial class FinanceAdjustmentService
{
    private static Guid FinePostingKey(long tenantId, long fineId, string kind)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:StudentFine:" + kind + ":" + tenantId + ":" + fineId));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public async Task<ApiResponse<PostedStudentFineInvoiceDto>> PostFineInvoiceAsync(long fineId,
        PostStudentFineInvoiceRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<PostedStudentFineInvoiceDto>("Institution administrator permission required.", 403);
        if (fineId <= 0 || request == null || request.AccountingPeriodId <= 0 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<PostedStudentFineInvoiceDto>("Fine, accounting period and row version are required.");

        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var fine = await StudentFines.FirstOrDefaultAsync(x => x.Id == fineId, ct);
            if (fine == null) return Error<PostedStudentFineInvoiceDto>("Fine not found.", 404);

            // Ignore query filters deliberately: a cancelled/soft-deleted financial source must
            // never allow the same assessed fine to be invoiced a second time.
            var priorLine = await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.StudentFineId == fine.Id, ct);
            if (priorLine != null)
            {
                var priorEnrollment = await Enrollments.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == fine.StudentEnrollmentId, ct);
                var priorInvoice = await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == priorLine.StudentInvoiceId, ct);
                var priorJournal = priorInvoice?.JournalId == null ? null :
                    await db.Journals.IgnoreQueryFilters().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == priorInvoice.JournalId, ct);
                var priorJournalLines = priorJournal == null ? new List<JournalLine>() :
                    await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.TenantId == tenant && x.JournalId == priorJournal.Id).ToListAsync(ct);
                if (priorLine.IsDeleted || priorInvoice == null || priorInvoice.IsDeleted ||
                    priorJournal == null || priorEnrollment == null ||
                    !InstitutionalPostingJournalIntegrity.Matches(priorJournal, priorJournalLines, tenant,
                        "StudentInvoice", priorInvoice.Id, priorEnrollment.CampusId,
                        priorEnrollment.StudentId, fine.Amount) ||
                    await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.State == JournalState.Posted && x.ReversalOfJournalId == priorJournal.Id, ct) ||
                    priorInvoice.StudentEnrollmentId != fine.StudentEnrollmentId ||
                    priorInvoice.TotalAmount != fine.Amount || priorInvoice.FineAmount != fine.Amount ||
                    priorInvoice.Subtotal != 0m || priorInvoice.DiscountAmount != 0m ||
                    priorInvoice.PaidAmount != 0m || priorInvoice.DueAmount != fine.Amount ||
                    priorInvoice.State != InvoiceState.Issued || priorLine.FineAmount != fine.Amount ||
                    priorLine.NetAmount != fine.Amount || priorLine.Amount != 0m ||
                    priorJournal.AccountingPeriodId != request.AccountingPeriodId ||
                    priorJournal.SourceType != "StudentInvoice" || priorJournal.SourceId != priorInvoice.Id)
                    return Error<PostedStudentFineInvoiceDto>("Prior fine invoice or journal needs reconciliation.", 409);
                return ApiResponse<PostedStudentFineInvoiceDto>.SuccessResponse(new PostedStudentFineInvoiceDto
                {
                    FineId = fine.Id, InvoiceReference = priorInvoice.PublicId,
                    InvoiceNumber = priorInvoice.InvoiceNumber,
                    JournalReference = priorJournal.PublicId, JournalNumber = priorJournal.JournalNumber,
                    Amount = fine.Amount, AlreadyPosted = true
                }, "Existing posted fine invoice returned.");
            }

            if (!Matches(fine.RowVersion, request.RowVersion) || fine.IsWaived ||
                fine.Amount <= 0m || fine.Amount > 999999999999m || !Scale(fine.Amount, 2) ||
                fine.FineDate == default || fine.FineDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Error<PostedStudentFineInvoiceDto>("Fine was changed, waived or has invalid terms.", 409);
            if (!fine.CreatedBy.HasValue || fine.CreatedBy.Value == user.UserId)
                return Error<PostedStudentFineInvoiceDto>("A different institution administrator must approve and post the assessed fine.", 403);

            var enrollment = await Enrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == fine.StudentEnrollmentId, ct);
            var rule = await Fines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fine.FineRuleId, ct);
            if (enrollment == null || rule == null || rule.IsDaily || !rule.FeeHeadId.HasValue ||
                !await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == enrollment.CampusId && !x.IsDeleted, ct) ||
                !await db.Students.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == enrollment.StudentId && !x.IsDeleted, ct))
                return Error<PostedStudentFineInvoiceDto>("Fine rule, campus or student provenance invalid.", 409);
            if (rule.Amount != fine.Amount)
                return Error<PostedStudentFineInvoiceDto>("Fine amount differs from its immutable rule.", 409);

            var head = await Heads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rule.FeeHeadId.Value, ct);
            if (head == null || !head.IsActive || !head.IncomeAccountId.HasValue || !head.ReceivableAccountId.HasValue ||
                head.IncomeAccountId == head.ReceivableAccountId ||
                await db.FeeStructureLines.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.FeeHeadId == head.Id && !x.IsDeleted, ct) ||
                await db.StudentFeeInstallmentPlanLines.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.FeeHeadId == head.Id && !x.IsDeleted, ct) ||
                await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.FeeHeadId == head.Id && !x.StudentFineId.HasValue, ct))
                return Error<PostedStudentFineInvoiceDto>("Use a dedicated fine fee head with configured income and receivable accounts.", 409);
            var accountIds = new[] { head.IncomeAccountId.Value, head.ReceivableAccountId.Value };
            var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
                accountIds.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                x.AllowsPosting && x.CurrencyCode == "BDT").ToListAsync(ct);
            if (!accounts.Any(x => x.Id == head.IncomeAccountId && x.AccountType == AccountType.Income) ||
                !accounts.Any(x => x.Id == head.ReceivableAccountId && x.AccountType == AccountType.Asset))
                return Error<PostedStudentFineInvoiceDto>("Fine income and receivable ledgers are invalid.", 409);

            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= fine.FineDate && x.EndDate >= fine.FineDate, ct);
            if (period == null)
                return Error<PostedStudentFineInvoiceDto>("Open accounting period must cover the fine date.", 409);
            var invoiceKey = FinePostingKey(tenant, fine.Id, "Invoice");
            var journalKey = FinePostingKey(tenant, fine.Id, "Journal");
            if (await db.StudentInvoices.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                    x.ClientRequestId == invoiceKey, ct) ||
                await db.Journals.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                    x.IdempotencyKey == journalKey, ct))
                return Error<PostedStudentFineInvoiceDto>("Unlinked fine financial history exists; reconcile first.", 409);

            var now = clock.GetUtcNow().UtcDateTime;
            var invoiceRef = Guid.NewGuid();
            var invoice = new StudentInvoice
            {
                TenantId = tenant, PublicId = invoiceRef, ClientRequestId = invoiceKey,
                StudentEnrollmentId = enrollment.Id,
                InvoiceNumber = "FIN-" + invoiceRef.ToString("N").ToUpperInvariant(),
                InvoiceDate = fine.FineDate, DueDate = fine.FineDate,
                Subtotal = 0m, DiscountAmount = 0m, FineAmount = fine.Amount,
                TotalAmount = fine.Amount, PaidAmount = 0m, DueAmount = fine.Amount,
                CurrencyCode = "BDT", State = InvoiceState.Issued,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.StudentInvoices.Add(invoice);
            await db.SaveChangesAsync(ct);
            db.StudentInvoiceLines.Add(new StudentInvoiceLine
            {
                TenantId = tenant, StudentInvoiceId = invoice.Id, StudentFineId = fine.Id,
                FeeHeadId = head.Id, FeeHeadCodeSnapshot = head.Code, FeeHeadNameSnapshot = head.Name,
                Description = "Fixed fine — " + rule.Name,
                Amount = 0m, DiscountAmount = 0m, FineAmount = fine.Amount, NetAmount = fine.Amount,
                CreatedAt = now, CreatedBy = user.UserId
            });
            var journalRef = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = tenant, PublicId = journalRef, IdempotencyKey = journalKey,
                JournalNumber = "JRN-" + journalRef.ToString("N").ToUpperInvariant(),
                JournalDate = fine.FineDate, AccountingPeriodId = period.Id,
                CampusId = enrollment.CampusId, ReferenceNumber = invoice.InvoiceNumber,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "StudentInvoice", SourceId = invoice.Id,
                Description = "Fixed fine invoice " + invoice.InvoiceNumber,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine
                {
                    TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                    AccountId = head.ReceivableAccountId.Value, CampusId = enrollment.CampusId,
                    StudentId = enrollment.StudentId, AcademicProgramId = enrollment.AcademicProgramId,
                    DebitAmount = fine.Amount, Description = "Assessed fine receivable",
                    CreatedAt = now, CreatedBy = user.UserId
                },
                new JournalLine
                {
                    TenantId = tenant, JournalId = journal.Id, LineNo = 2,
                    AccountId = head.IncomeAccountId.Value, CampusId = enrollment.CampusId,
                    StudentId = enrollment.StudentId, AcademicProgramId = enrollment.AcademicProgramId,
                    FeeHeadId = head.Id, CreditAmount = fine.Amount,
                    Description = "Assessed fine income", CreatedAt = now, CreatedBy = user.UserId
                });
            invoice.JournalId = journal.Id;
            invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId, Action = "PostStudentFineInvoice",
                EntityName = "StudentFine", EntityId = fine.Id,
                OldValue = "Administrative fine", NewValue = "Invoice " + invoice.InvoiceNumber,
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostedStudentFineInvoiceDto>.SuccessResponse(new PostedStudentFineInvoiceDto
            {
                FineId = fine.Id, InvoiceReference = invoice.PublicId,
                InvoiceNumber = invoice.InvoiceNumber,
                JournalReference = journal.PublicId, JournalNumber = journal.JournalNumber,
                Amount = fine.Amount
            }, "Fine invoice and balanced GL journal posted.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PostedStudentFineInvoiceDto>("Fine or financial configuration changed; reload.", 409); }
        catch (DbUpdateException)
        { return Error<PostedStudentFineInvoiceDto>("Conflicting fine posting; reconcile and retry.", 409); }
    }
}
