using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Library;

public sealed partial class LibraryLiabilityService
{
    private sealed record ReversalSource(LibraryLiability Charge, StudentInvoice Invoice,
        Journal Journal, JournalLine[] Lines);

    private async Task<ReversalSource?> ReversalSourceAsync(Guid reference, CancellationToken ct)
    {
        var tenant = user.TenantId;
        var charge = await db.LibraryLiabilities.FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.PublicId == reference && !x.IsDeleted, ct);
        if (charge == null || charge.State is not (LibraryLiabilityState.Posted or LibraryLiabilityState.Reversed) ||
            !charge.StudentInvoiceId.HasValue) return null;
        var invoice = await db.StudentInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == charge.StudentInvoiceId.Value && !x.IsDeleted, ct);
        if (invoice == null || invoice.ClientRequestId != charge.ClientRequestId ||
            invoice.StudentEnrollmentId != charge.StudentEnrollmentId || invoice.CurrencyCode != "BDT" ||
            invoice.TotalAmount != charge.Amount || invoice.Subtotal != charge.Amount ||
            invoice.FineAmount != 0m || invoice.DiscountAmount != 0m || !invoice.JournalId.HasValue)
            return null;
        var invoiceLines = await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentInvoiceId == invoice.Id).Take(3).ToArrayAsync(ct);
        if (invoiceLines.Length != 1 || invoiceLines[0].IsDeleted ||
            invoiceLines[0].Amount != charge.Amount || invoiceLines[0].NetAmount != charge.Amount ||
            invoiceLines[0].DiscountAmount != 0m || invoiceLines[0].FineAmount != 0m ||
            invoiceLines[0].FeeHeadCodeSnapshot?.StartsWith("LIB-", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == invoice.JournalId.Value && !x.IsDeleted, ct);
        if (journal == null || journal.SourceType != "StudentInvoice" || journal.SourceId != invoice.Id ||
            journal.State != JournalState.Posted || journal.CampusId != charge.CampusId ||
            journal.CurrencyCode != "BDT" || journal.IdempotencyKey != charge.ClientRequestId)
            return null;
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.JournalId == journal.Id).OrderBy(x => x.LineNo).Take(3).ToArrayAsync(ct);
        if (lines.Length != 2 || lines.Any(x => x.IsDeleted || x.CampusId != charge.CampusId) ||
            lines.Count(x => x.DebitAmount == charge.Amount && x.CreditAmount == 0m) != 1 ||
            lines.Count(x => x.CreditAmount == charge.Amount && x.DebitAmount == 0m &&
                x.FeeHeadId == invoiceLines[0].FeeHeadId) != 1)
            return null;
        return new ReversalSource(charge, invoice, journal, lines);
    }

    private async Task<bool> HasLibraryPaymentHistoryAsync(long invoiceId, CancellationToken ct) =>
        await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.StudentInvoiceId == invoiceId, ct);

    private static LibraryReversalDto ToReversalDto(Journal journal, Guid liabilityReference) => new()
    {
        Reference = journal.PublicId, LiabilityReference = liabilityReference,
        JournalNumber = journal.JournalNumber, State = (int)journal.State,
        ReversalDate = journal.JournalDate, RowVersion = Convert.ToBase64String(journal.RowVersion)
    };

    public async Task<ApiResponse<LibraryReversalDto>> CurrentReversalAsync(Guid liabilityReference,
        CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryReversalDto>("Institution administrator permission required.", 403);
        var source = await ReversalSourceAsync(liabilityReference, ct);
        if (source == null) return Error<LibraryReversalDto>("Posted Library charge not found or requires reconciliation.", 404);
        var journal = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.SourceType == "LibraryLiabilityReversal" && x.SourceId == source.Charge.Id && !x.IsDeleted)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (journal == null) return ApiResponse<LibraryReversalDto>.SuccessResponse(new LibraryReversalDto
        { LiabilityReference = liabilityReference });
        return ApiResponse<LibraryReversalDto>.SuccessResponse(ToReversalDto(journal, liabilityReference));
    }

    public async Task<ApiResponse<LibraryReversalDto>> RequestReversalAsync(Guid reference,
        RequestLibraryReversalDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryReversalDto>("Institution administrator permission required.", 403);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default || request.ReversalDate > today ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            request.RowVersion?.Length is < 1 or > 256)
            return Error<LibraryReversalDto>("Reference, open period, reversal date, reason and concurrency token required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await ReversalSourceAsync(reference, ct);
            if (source == null) return Error<LibraryReversalDto>("Library financial provenance requires reconciliation.", 409);
            var existing = await db.Journals.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.SourceType == "LibraryLiabilityReversal" && x.SourceId == source.Charge.Id && !x.IsDeleted)
                .OrderByDescending(x => x.Id).Take(101).ToArrayAsync(ct);
            if (existing.Length > 100) return Error<LibraryReversalDto>("Reversal history requires reconciliation.", 409);
            var replay = existing.FirstOrDefault(x => x.IdempotencyKey == request.ClientRequestId);
            if (replay != null)
            {
                if (replay.ReversalOfJournalId != source.Journal.Id ||
                    replay.JournalDate != request.ReversalDate ||
                    replay.AccountingPeriodId != request.AccountingPeriodId ||
                    replay.Description != request.Reason.Trim())
                    return Error<LibraryReversalDto>("Request ID was already used with different terms.", 409);
                return ApiResponse<LibraryReversalDto>.SuccessResponse(ToReversalDto(replay, reference));
            }
            if (existing.Any(x => x.State is JournalState.Submitted or JournalState.Approved or JournalState.Posted) ||
                await db.Journals.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                    x.IdempotencyKey == request.ClientRequestId, ct))
                return Error<LibraryReversalDto>("Reversal already pending or request ID belongs to another journal.", 409);
            if (source.Charge.State != LibraryLiabilityState.Posted ||
                !Version(source.Charge.RowVersion, request.RowVersion) ||
                source.Invoice.State != InvoiceState.Issued || source.Invoice.PaidAmount != 0m ||
                source.Invoice.DueAmount != source.Charge.Amount ||
                request.ReversalDate < source.Invoice.InvoiceDate ||
                await HasLibraryPaymentHistoryAsync(source.Invoice.Id, ct))
                return Error<LibraryReversalDto>("Only unchanged, entirely unpaid Library invoices can be reversed.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate, ct);
            if (period == null) return Error<LibraryReversalDto>("Reversal needs an open accounting period.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var journalReference = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = tenant, PublicId = journalReference, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "LRV-" + journalReference.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = period.Id, CampusId = source.Charge.CampusId,
                ReferenceNumber = source.Invoice.InvoiceNumber, CurrencyCode = "BDT",
                SourceType = "LibraryLiabilityReversal", SourceId = source.Charge.Id,
                ReversalOfJournalId = source.Journal.Id, State = JournalState.Submitted,
                Description = request.Reason.Trim(), CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(source.Lines.Select(x => new JournalLine
            {
                TenantId = tenant, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = x.CampusId, StudentId = x.StudentId,
                AcademicProgramId = x.AcademicProgramId, EmployeeId = x.EmployeeId,
                FeeHeadId = x.FeeHeadId, DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                Description = "Library charge reversal " + source.Invoice.InvoiceNumber,
                CreatedBy = user.UserId, CreatedAt = now
            }));
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId, Action = "RequestLibraryLiabilityReversal",
                EntityName = "LibraryLiability", EntityId = source.Charge.Id,
                NewValue = reversal.JournalNumber, OccurredAt = now,
                RequestId = request.ClientRequestId.ToString("N"), IsSuccess = true
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<LibraryReversalDto>.SuccessResponse(ToReversalDto(reversal, reference));
        }
        catch (DbUpdateConcurrencyException) { return Error<LibraryReversalDto>("Financial state changed; reload.", 409); }
        catch (DbUpdateException) { return Error<LibraryReversalDto>("Conflicting reversal; reload.", 409); }
    }

    public async Task<ApiResponse<LibraryReversalDto>> ReviewReversalAsync(Guid reference,
        Guid reversalReference, ReviewLibraryReversalDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryReversalDto>("Institution administrator permission required.", 403);
        if (reference == Guid.Empty || reversalReference == Guid.Empty || request == null ||
            request.RowVersion?.Length is < 1 or > 256 ||
            (!request.Approve && (string.IsNullOrWhiteSpace(request.RejectionReason) ||
                request.RejectionReason.Trim().Length > 1000)))
            return Error<LibraryReversalDto>("Review token and rejection reason (when rejecting) are required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await ReversalSourceAsync(reference, ct);
            if (source == null) return Error<LibraryReversalDto>("Original Library posting needs reconciliation.", 409);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == reversalReference && x.SourceType == "LibraryLiabilityReversal" &&
                x.SourceId == source.Charge.Id && !x.IsDeleted, ct);
            if (reversal == null || reversal.ReversalOfJournalId != source.Journal.Id)
                return Error<LibraryReversalDto>("Reversal request not found.", 404);
            if (reversal.State == (request.Approve ? JournalState.Posted : JournalState.Rejected))
                return ApiResponse<LibraryReversalDto>.SuccessResponse(ToReversalDto(reversal, reference));
            if (reversal.State != JournalState.Submitted || !Version(reversal.RowVersion, request.RowVersion))
                return Error<LibraryReversalDto>("Reversal changed; reload.", 409);
            if (!reversal.CreatedBy.HasValue || reversal.CreatedBy == user.UserId ||
                source.Charge.ReviewedByUserId == user.UserId)
                return Error<LibraryReversalDto>("Independent administrator review required.", 403);
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Approve)
            {
                if (source.Charge.State != LibraryLiabilityState.Posted ||
                    source.Invoice.State != InvoiceState.Issued || source.Invoice.PaidAmount != 0m ||
                    source.Invoice.DueAmount != source.Charge.Amount ||
                    await HasLibraryPaymentHistoryAsync(source.Invoice.Id, ct) ||
                    await db.Journals.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                        x.Id != reversal.Id && x.ReversalOfJournalId == source.Journal.Id &&
                        x.State == JournalState.Posted, ct))
                    return Error<LibraryReversalDto>("Invoice paid, changed or already reversed.", 409);
                var periodOpen = await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == reversal.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= reversal.JournalDate && x.EndDate >= reversal.JournalDate, ct);
                if (!periodOpen) return Error<LibraryReversalDto>("Reversal accounting period closed.", 409);
                var actual = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.JournalId == reversal.Id)
                    .OrderBy(x => x.LineNo).Take(3).ToArrayAsync(ct);
                if (actual.Length != source.Lines.Length || actual.Where((x, i) =>
                    x.IsDeleted || x.LineNo != source.Lines[i].LineNo ||
                    x.AccountId != source.Lines[i].AccountId || x.CampusId != source.Lines[i].CampusId ||
                    x.StudentId != source.Lines[i].StudentId || x.EmployeeId != source.Lines[i].EmployeeId ||
                    x.FeeHeadId != source.Lines[i].FeeHeadId ||
                    x.AcademicProgramId != source.Lines[i].AcademicProgramId ||
                    x.DebitAmount != source.Lines[i].CreditAmount ||
                    x.CreditAmount != source.Lines[i].DebitAmount).Any())
                    return Error<LibraryReversalDto>("Reversal entries no longer mirror original financial posting.", 409);
                reversal.State = JournalState.Posted;
                reversal.ApprovedByUserId = user.UserId; reversal.PostedByUserId = user.UserId;
                reversal.ApprovedAt = now; reversal.PostedAt = now;
                source.Invoice.State = InvoiceState.Cancelled;
                source.Invoice.UpdatedAt = now; source.Invoice.UpdatedBy = user.UserId;
                source.Charge.State = LibraryLiabilityState.Reversed;
                source.Charge.UpdatedAt = now; source.Charge.UpdatedBy = user.UserId;
            }
            else reversal.State = JournalState.Rejected;
            reversal.UpdatedAt = now; reversal.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant, UserId = user.UserId,
                Action = request.Approve ? "ApproveLibraryLiabilityReversal" : "RejectLibraryLiabilityReversal",
                EntityName = "LibraryLiability", EntityId = source.Charge.Id,
                OldValue = source.Invoice.InvoiceNumber,
                NewValue = request.Approve ? reversal.JournalNumber : request.RejectionReason!.Trim(),
                OccurredAt = now, IsSuccess = true
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<LibraryReversalDto>.SuccessResponse(ToReversalDto(reversal, reference));
        }
        catch (DbUpdateConcurrencyException) { return Error<LibraryReversalDto>("Reversal review conflicted; reload.", 409); }
        catch (DbUpdateException) { return Error<LibraryReversalDto>("Financial state changed during approval; reload.", 409); }
    }
}
