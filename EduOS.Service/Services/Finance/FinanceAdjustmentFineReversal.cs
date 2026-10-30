using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// A posted fine can only be waived with a separate, two-person approved contra journal.
/// The original invoice, invoice lines and original posted journal remain immutable evidence.
/// Paid fines require the separate refund/credit-note process and cannot enter this workflow.
/// </summary>
public sealed partial class FinanceAdjustmentService
{
    private sealed record FineReversalSource(StudentFine Fine, StudentInvoice Invoice,
        Journal Journal, StudentEnrollment Enrollment, JournalLine[] Lines);

    private async Task<FineReversalSource?> FindFineReversalSource(long fineId, CancellationToken ct)
    {
        var tenant = user.TenantId;
        var fine = await StudentFines.FirstOrDefaultAsync(x => x.Id == fineId, ct);
        if (fine == null) return null;
        var invoiceLines = await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentFineId == fine.Id).Take(3).ToListAsync(ct);
        if (invoiceLines.Count != 1 || invoiceLines[0].IsDeleted) return null;
        var line = invoiceLines[0];
        var invoice = await db.StudentInvoices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == line.StudentInvoiceId, ct);
        var enrollment = await Enrollments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fine.StudentEnrollmentId, ct);
        if (invoice == null || invoice.IsDeleted || enrollment == null || !invoice.JournalId.HasValue ||
            invoice.StudentEnrollmentId != fine.StudentEnrollmentId || invoice.CurrencyCode != "BDT" ||
            invoice.TotalAmount != fine.Amount || invoice.FineAmount != fine.Amount ||
            invoice.Subtotal != 0m || invoice.DiscountAmount != 0m ||
            line.FineAmount != fine.Amount || line.NetAmount != fine.Amount ||
            line.Amount != 0m || line.DiscountAmount != 0m)
            return null;
        if (await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.StudentInvoiceId == invoice.Id && x.Id != line.Id, ct))
            return null;
        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == invoice.JournalId.Value, ct);
        var lines = journal == null ? [] : await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.JournalId == journal.Id).Take(3).ToArrayAsync(ct);
        if (journal == null || journal.IsDeleted || lines.Length != 2 || lines.Any(x => x.IsDeleted) ||
            !InstitutionalPostingJournalIntegrity.Matches(journal, lines, tenant,
                "StudentInvoice", invoice.Id, enrollment.CampusId, enrollment.StudentId, fine.Amount) ||
            lines.Count(x => x.DebitAmount == fine.Amount && x.CreditAmount == 0m) != 1 ||
            lines.Count(x => x.CreditAmount == fine.Amount && x.DebitAmount == 0m &&
                x.FeeHeadId == line.FeeHeadId) != 1)
            return null;
        return new FineReversalSource(fine, invoice, journal, enrollment, lines);
    }

    private async Task<bool> FineHasPaymentHistory(long tenant, long invoiceId, CancellationToken ct) =>
        await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.StudentInvoiceId == invoiceId, ct);

    private static StudentFineReversalDto FineReversalResult(FineReversalSource source, Journal reversal,
        bool replay = false) => new()
    {
        FineId = source.Fine.Id, InvoiceReference = source.Invoice.PublicId,
        ReversalReference = reversal.PublicId, JournalNumber = reversal.JournalNumber,
        State = reversal.State, Amount = source.Fine.Amount, AlreadyProcessed = replay,
        RowVersion = Version(reversal.RowVersion)
    };

    public async Task<ApiResponse<StudentFineReversalDto>> RequestFineReversalAsync(long fineId,
        RequestStudentFineReversalDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<StudentFineReversalDto>("Institution administrator permission required.", 403);
        if (fineId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default ||
            request.ReversalDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<StudentFineReversalDto>("Reversal reference, open period, date, reason and fine row version required.");

        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await FindFineReversalSource(fineId, ct);
            if (source == null) return Error<StudentFineReversalDto>("Posted fine source needs reconciliation.", 409);
            var existing = await db.Journals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.SourceType == "StudentFineReversal" &&
                    x.SourceId == fineId && !x.IsDeleted)
                .OrderByDescending(x => x.Id).Take(101).ToListAsync(ct);
            if (existing.Count > 100) return Error<StudentFineReversalDto>("Fine reversal history needs reconciliation.", 409);
            var replay = existing.FirstOrDefault(x => x.IdempotencyKey == request.ClientRequestId);
            if (replay != null)
            {
                if (replay.ReversalOfJournalId != source.Journal.Id ||
                    replay.JournalDate != request.ReversalDate ||
                    replay.AccountingPeriodId != request.AccountingPeriodId ||
                    replay.Description != request.Reason.Trim())
                    return Error<StudentFineReversalDto>("Reversal reference was used with different terms.", 409);
                return ApiResponse<StudentFineReversalDto>.SuccessResponse(
                    FineReversalResult(source, replay, true), "Existing fine reversal request returned.");
            }
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.IdempotencyKey == request.ClientRequestId, ct))
                return Error<StudentFineReversalDto>("Reversal reference already belongs to another journal.", 409);
            if (!Matches(source.Fine.RowVersion, request.RowVersion) || source.Fine.IsWaived ||
                source.Invoice.State != InvoiceState.Issued || source.Invoice.PaidAmount != 0m ||
                source.Invoice.DueAmount != source.Fine.Amount ||
                request.ReversalDate < source.Invoice.InvoiceDate ||
                await FineHasPaymentHistory(tenant, source.Invoice.Id, ct))
                return Error<StudentFineReversalDto>("Only an unchanged, entirely unpaid issued fine can be reversed.", 409);
            if (existing.Any(x => x.State is JournalState.Submitted or JournalState.Approved or JournalState.Posted))
                return Error<StudentFineReversalDto>("This fine already has a pending or posted reversal.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, ct);
            if (period == null) return Error<StudentFineReversalDto>("Reversal date requires an open accounting period.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var reference = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = tenant, PublicId = reference, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FRV-" + reference.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = period.Id,
                CampusId = source.Enrollment.CampusId, ReferenceNumber = source.Invoice.InvoiceNumber,
                CurrencyCode = "BDT", State = JournalState.Submitted,
                SourceType = "StudentFineReversal", SourceId = fineId,
                ReversalOfJournalId = source.Journal.Id, Description = request.Reason.Trim(),
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            // Persist maker identity even under explicitly anonymous test/import contexts.
            if (reversal.CreatedBy != user.UserId)
            {
                reversal.CreatedBy = user.UserId;
                await db.SaveChangesAsync(ct);
            }
            db.JournalLines.AddRange(source.Lines.OrderBy(x => x.LineNo).Select(x => new JournalLine
            {
                TenantId = tenant, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = x.CampusId, StudentId = x.StudentId,
                EmployeeId = x.EmployeeId, AcademicProgramId = x.AcademicProgramId,
                FeeHeadId = x.FeeHeadId, DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                Description = "Fine reversal " + source.Invoice.InvoiceNumber,
                CreatedBy = user.UserId, CreatedAt = now
            }));
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId, Action = "RequestStudentFineReversal",
                EntityName = "StudentFine", EntityId = fineId,
                OldValue = source.Invoice.InvoiceNumber, NewValue = reversal.JournalNumber,
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentFineReversalDto>.SuccessResponse(
                FineReversalResult(source, reversal), "Fine reversal submitted for independent review.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentFineReversalDto>("Fine changed during reversal request; reload.", 409); }
        catch (DbUpdateException)
        { return Error<StudentFineReversalDto>("Conflicting reversal request; reload.", 409); }
    }

    public async Task<ApiResponse<StudentFineReversalDto>> ReviewFineReversalAsync(long fineId,
        Guid reversalReference, ReviewStudentFineReversalDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<StudentFineReversalDto>("Institution administrator permission required.", 403);
        if (fineId <= 0 || reversalReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256 ||
            (!request.Approve && (string.IsNullOrWhiteSpace(request.RejectionReason) ||
                request.RejectionReason.Trim().Length > 1000)))
            return Error<StudentFineReversalDto>("Reversal reference, row version and rejection reason (when applicable) required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await FindFineReversalSource(fineId, ct);
            if (source == null) return Error<StudentFineReversalDto>("Fine financial source needs reconciliation.", 409);
            var reversal = await db.Journals.FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reversalReference &&
                x.SourceType == "StudentFineReversal" && x.SourceId == fineId && !x.IsDeleted, ct);
            if (reversal == null || reversal.ReversalOfJournalId != source.Journal.Id)
                return Error<StudentFineReversalDto>("Fine reversal request not found.", 404);
            if (reversal.State == (request.Approve ? JournalState.Posted : JournalState.Rejected))
                return ApiResponse<StudentFineReversalDto>.SuccessResponse(
                    FineReversalResult(source, reversal, true), "Fine reversal was already reviewed.");
            if (reversal.State != JournalState.Submitted ||
                !Matches(reversal.RowVersion, request.RowVersion))
                return Error<StudentFineReversalDto>("Reversal already reviewed or changed; reload.", 409);
            if (!reversal.CreatedBy.HasValue || reversal.CreatedBy == user.UserId)
                return Error<StudentFineReversalDto>("A different institution administrator must review this reversal.", 403);
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Approve)
            {
                if (source.Fine.IsWaived || source.Invoice.State != InvoiceState.Issued ||
                    source.Invoice.PaidAmount != 0m || source.Invoice.DueAmount != source.Fine.Amount ||
                    await FineHasPaymentHistory(tenant, source.Invoice.Id, ct) ||
                    await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id != reversal.Id &&
                        x.ReversalOfJournalId == source.Journal.Id && x.State == JournalState.Posted, ct))
                    return Error<StudentFineReversalDto>("Fine was paid, changed or previously reversed.", 409);
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == reversal.AccountingPeriodId &&
                    !x.IsDeleted && !x.IsClosed && x.StartDate <= reversal.JournalDate &&
                    x.EndDate >= reversal.JournalDate, ct);
                if (period == null) return Error<StudentFineReversalDto>("Reversal accounting period has closed.", 409);
                var contra = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.JournalId == reversal.Id).Take(3).ToArrayAsync(ct);
                var original = source.Lines.OrderBy(x => x.LineNo).ToArray();
                contra = contra.OrderBy(x => x.LineNo).ToArray();
                if (contra.Length != original.Length || reversal.CampusId != source.Enrollment.CampusId ||
                    reversal.CurrencyCode != "BDT" || original.Where((x, i) =>
                        contra[i].IsDeleted || contra[i].LineNo != x.LineNo ||
                        contra[i].AccountId != x.AccountId || contra[i].CampusId != x.CampusId ||
                        contra[i].StudentId != x.StudentId || contra[i].FeeHeadId != x.FeeHeadId ||
                        contra[i].AcademicProgramId != x.AcademicProgramId ||
                        contra[i].DebitAmount != x.CreditAmount ||
                        contra[i].CreditAmount != x.DebitAmount).Any())
                    return Error<StudentFineReversalDto>("Contra journal lines differ from the posted source.", 409);
                reversal.State = JournalState.Posted;
                reversal.ApprovedByUserId = user.UserId; reversal.ApprovedAt = now;
                reversal.PostedByUserId = user.UserId; reversal.PostedAt = now;
                source.Invoice.State = InvoiceState.Cancelled;
                source.Invoice.UpdatedBy = user.UserId; source.Invoice.UpdatedAt = now;
                source.Fine.IsWaived = true; source.Fine.WaivedByUserId = user.UserId;
                source.Fine.UpdatedBy = user.UserId; source.Fine.UpdatedAt = now;
            }
            else
            {
                reversal.State = JournalState.Rejected;
            }
            reversal.UpdatedBy = user.UserId; reversal.UpdatedAt = now;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId,
                Action = request.Approve ? "ApproveStudentFineReversal" : "RejectStudentFineReversal",
                EntityName = "StudentFine", EntityId = fineId,
                OldValue = source.Invoice.InvoiceNumber,
                NewValue = request.Approve ? reversal.JournalNumber : request.RejectionReason!.Trim(),
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentFineReversalDto>.SuccessResponse(
                FineReversalResult(source, reversal),
                request.Approve ? "Fine invoice reversed through balanced GL contra entry." : "Fine reversal rejected.");
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentFineReversalDto>("Reversal changed during review; reload.", 409); }
        catch (DbUpdateException)
        { return Error<StudentFineReversalDto>("Reversal conflicted with a payment or financial update; reload.", 409); }
    }
}
