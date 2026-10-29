using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Reverses an unpaid payroll accrual through an independently approved contra journal.
/// The original posted run, payroll snapshots and journal are never changed.
/// </summary>
public sealed partial class PayrollJournalPostingService
{
    private static PayrollReversalResultDto ReversalResult(PayrollRun run, Journal journal, bool replay = false) => new()
    {
        PayrollReference = run.PublicId, ReversalReference = journal.PublicId,
        State = journal.State, RowVersion = Convert.ToBase64String(journal.RowVersion),
        AlreadyProcessed = replay
    };

    private async Task<(PayrollRun? Run, Journal? Original, List<JournalLine> Lines, string? Error)>
        ReversalSourceAsync(Guid reference, CancellationToken ct)
    {
        var tenant = user.TenantId;
        var run = await db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.PublicId == reference && !x.IsDeleted, ct);
        if (run == null) return (null, null, [], "Payroll run not found.");
        if (run.State != PayrollRunState.Posted || !run.JournalId.HasValue)
            return (run, null, [], "Only posted payroll accruals can be reversed.");
        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == run.JournalId.Value, ct);
        if (journal == null || journal.IsDeleted || journal.SourceType != "PayrollRun" ||
            journal.SourceId != run.Id || journal.State != JournalState.Posted ||
            journal.ReversalOfJournalId.HasValue || journal.CampusId != run.CampusId ||
            journal.CurrencyCode != "BDT" || journal.JournalDate != run.PeriodEnd)
            return (run, journal, [], "Original payroll journal requires reconciliation.");
        if (await PayrollPostingHistoryIntegrity.HasDeletedLinesAsync(db, tenant, journal.Id, ct))
            return (run, journal, [], "Original payroll journal has deleted historical lines.");
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.JournalId == journal.Id && !x.IsDeleted).OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
        if (lines.Count is < 2 or > 100 || lines.Select((line, idx) => line.LineNo == idx + 1).Any(ok => !ok) ||
            lines.Any(x => x.CampusId != run.CampusId || x.AccountId <= 0 ||
                !Money(x.DebitAmount) || !Money(x.CreditAmount) ||
                (x.DebitAmount > 0m) == (x.CreditAmount > 0m)) ||
            lines.Sum(x => x.DebitAmount) <= 0m ||
            lines.Sum(x => x.DebitAmount) != lines.Sum(x => x.CreditAmount))
            return (run, journal, lines, "Original payroll journal is not a balanced immutable accrual.");
        // Pending salary disbursement requests, successful/linked payment history and loan
        // recoveries must be cleared before reversing the accrual. Never strand a request.
        if (await db.PayrollPayments.IgnoreQueryFilters().AsNoTracking().AnyAsync(payment =>
            payment.TenantId == tenant &&
            (payment.State == PaymentState.Initiated || payment.State == PaymentState.AwaitingVerification ||
             payment.State == PaymentState.Successful || payment.JournalId.HasValue) &&
            db.PayrollEmployees.IgnoreQueryFilters().Any(employee =>
                employee.TenantId == tenant && employee.PayrollRunId == run.Id &&
                employee.Id == payment.PayrollEmployeeId), ct))
            return (run, journal, lines, "Payroll has pending or posted payment history; reconcile disbursements before reversal.");
        if (await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.JournalId == journal.Id, ct))
            return (run, journal, lines, "Payroll loan recoveries must be compensated before reversal.");
        return (run, journal, lines, null);
    }

    private async Task<string?> ReversalMatchesAsync(PayrollRun run, Journal original,
        IReadOnlyList<JournalLine> originalLines, Journal reversal, CancellationToken ct)
    {
        if (reversal.SourceType != "PayrollRunReversal" || reversal.SourceId != run.Id ||
            reversal.ReversalOfJournalId != original.Id || reversal.CampusId != run.CampusId ||
            reversal.CurrencyCode != "BDT" || reversal.JournalDate < original.JournalDate ||
            reversal.CreatedBy is null or <= 0 || reversal.IsDeleted)
            return "Payroll contra journal provenance is invalid.";
        if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id != reversal.Id && x.ReversalOfJournalId == original.Id &&
            (x.State == JournalState.Posted || x.State == JournalState.Reversed ||
             (!x.IsDeleted && x.State != JournalState.Rejected && x.State != JournalState.Cancelled)), ct))
            return "A separate active payroll reversal already exists.";
        var contra = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.JournalId == reversal.Id)
            .OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
        if (contra.Count != originalLines.Count || contra.Any(x => x.IsDeleted))
            return "Contra journal lines are missing or were deleted.";
        for (var i = 0; i < originalLines.Count; i++)
        {
            var source = originalLines[i]; var line = contra[i];
            if (line.LineNo != source.LineNo || line.AccountId != source.AccountId ||
                line.CampusId != source.CampusId || line.EmployeeId != source.EmployeeId ||
                line.StudentId != source.StudentId || line.AcademicProgramId != source.AcademicProgramId ||
                line.FeeHeadId != source.FeeHeadId || line.DebitAmount != source.CreditAmount ||
                line.CreditAmount != source.DebitAmount || line.Description != source.Description)
                return "Contra lines must exactly negate the original payroll accrual.";
        }
        return null;
    }

    public async Task<ApiResponse<PayrollReversalResultDto>> RequestReversalAsync(Guid reference,
        RequestPayrollReversalDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollReversalResultDto>("Tenant administrator permission required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<PayrollReversalResultDto>("Reversal date, open period, reason, version and request key required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var replayRun = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted,ct);
            if (replayRun == null) return Error<PayrollReversalResultDto>("Payroll run not found.",404);
            var earlier = await db.Journals.IgnoreQueryFilters().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (earlier != null)
            {
                if (earlier.IsDeleted || earlier.SourceType != "PayrollRunReversal" || earlier.SourceId != replayRun.Id ||
                    earlier.ReversalOfJournalId != replayRun.JournalId || earlier.JournalDate != request.ReversalDate ||
                    earlier.AccountingPeriodId != request.AccountingPeriodId || earlier.Description != request.Reason.Trim())
                    return Error<PayrollReversalResultDto>("Request key belongs to a different operation.",409);
                return ApiResponse<PayrollReversalResultDto>.SuccessResponse(ReversalResult(replayRun,earlier,true),"Existing reversal returned.");
            }
            var (run, original, sourceLines, error) = await ReversalSourceAsync(reference, ct);
            if (run == null) return Error<PayrollReversalResultDto>("Payroll run not found.",404);
            if (error != null || original == null) return Error<PayrollReversalResultDto>(error ?? "Invalid journal.",409);
            if (!Version(run.RowVersion, request.RowVersion))
                return Error<PayrollReversalResultDto>("Payroll version changed; reload.",409);
            if (request.ReversalDate < original.JournalDate ||
                request.ReversalDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                return Error<PayrollReversalResultDto>("Reversal must not predate posting or be in the future.",409);
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ReversalOfJournalId == original.Id &&
                (x.State == JournalState.Posted || x.State == JournalState.Reversed ||
                 (!x.IsDeleted && x.State != JournalState.Rejected && x.State != JournalState.Cancelled)), ct))
                return Error<PayrollReversalResultDto>("An active reversal already exists.",409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= request.ReversalDate && x.EndDate >= request.ReversalDate,ct);
            if (period == null) return Error<PayrollReversalResultDto>("Open accounting period must cover reversal date.",409);
            var now = clock.GetUtcNow().UtcDateTime;
            var journalRef = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = journalRef, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "PRV-" + journalRef.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = period.Id,
                CampusId = run.CampusId, CurrencyCode = "BDT", State = JournalState.Submitted,
                SourceType = "PayrollRunReversal", SourceId = run.Id, ReversalOfJournalId = original.Id,
                ReferenceNumber = run.RunNumber, Description = request.Reason.Trim(),
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            // The audit interceptor may use HttpContext identity rather than the injected
            // command actor. Persist the explicit maker before second-person review.
            if (reversal.CreatedBy != user.UserId) reversal.CreatedBy = user.UserId;
            db.JournalLines.AddRange(sourceLines.Select(source => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = source.LineNo,
                AccountId = source.AccountId, CampusId = source.CampusId, EmployeeId = source.EmployeeId,
                StudentId = source.StudentId, AcademicProgramId = source.AcademicProgramId,
                FeeHeadId = source.FeeHeadId, DebitAmount = source.CreditAmount,
                CreditAmount = source.DebitAmount, Description = source.Description,
                CreatedAt = now, CreatedBy = user.UserId
            }));
            db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.UserId,
                Action = "RequestPayrollReversal", EntityName = "PayrollRun", EntityId = run.Id,
                OldValue = original.JournalNumber, NewValue = reversal.JournalNumber,
                OccurredAt = now });
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<PayrollReversalResultDto>.SuccessResponse(ReversalResult(run,reversal),
                "Payroll contra journal submitted for independent approval; no ledger posting occurred.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PayrollReversalResultDto>("Payroll reversal changed; reload.",409); }
        catch (DbUpdateException) { return Error<PayrollReversalResultDto>("Conflicting reversal; retry the same request key.",409); }
    }

    public async Task<ApiResponse<PayrollReversalResultDto>> ReviewReversalAsync(Guid reference,
        ReviewPayrollReversalDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollReversalResultDto>("Tenant administrator permission required.",403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            (!request.Approve && (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length > 500)))
            return Error<PayrollReversalResultDto>("Reversal version and rejection reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.SourceType == "PayrollRunReversal" && x.SourceId.HasValue &&
                (x.State == JournalState.Submitted || (!request.Approve && x.State == JournalState.Approved)) && db.PayrollRuns.Any(run =>
                    run.TenantId == user.TenantId && run.Id == x.SourceId && run.PublicId == reference),ct);
            if (reversal == null) return Error<PayrollReversalResultDto>("Reviewable reversal not found.",404);
            if (!Version(reversal.RowVersion,request.RowVersion)) return Error<PayrollReversalResultDto>("Reversal changed; reload.",409);
            if (reversal.CreatedBy is null or <= 0 || reversal.CreatedBy == user.UserId)
                return Error<PayrollReversalResultDto>("Maker and approver must be different administrators.",403);
            var run = await db.PayrollRuns.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted,ct);
            if (run == null || run.Id != reversal.SourceId) return Error<PayrollReversalResultDto>("Payroll run not found.",404);
            if (request.Approve)
            {
                var (sourceRun, original, sourceLines, error) = await ReversalSourceAsync(reference,ct);
                if (sourceRun == null || original == null || error != null)
                    return Error<PayrollReversalResultDto>(error ?? "Payroll journal missing.",409);
                if (await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == reversal.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= reversal.JournalDate && x.EndDate >= reversal.JournalDate,ct) == false)
                    return Error<PayrollReversalResultDto>("Reversal accounting period closed.",409);
                var invalid = await ReversalMatchesAsync(sourceRun,original,sourceLines,reversal,ct);
                if (invalid != null) return Error<PayrollReversalResultDto>(invalid,409);
            }
            var previousState = reversal.State;
            reversal.State = request.Approve ? JournalState.Approved : JournalState.Rejected;
            reversal.ApprovedByUserId = request.Approve ? user.UserId : null;
            reversal.ApprovedAt = request.Approve ? clock.GetUtcNow().UtcDateTime : null;
            reversal.UpdatedAt = clock.GetUtcNow().UtcDateTime;reversal.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId,UserId = user.UserId,
                Action = request.Approve ? "ApprovePayrollReversal" : "RejectPayrollReversal",
                EntityName = "Journal",EntityId = reversal.Id,OldValue = previousState.ToString(),
                NewValue = request.Approve ? "Approved" : "Rejected: " + request.Note!.Trim(),
                OccurredAt = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<PayrollReversalResultDto>.SuccessResponse(ReversalResult(run,reversal),
                request.Approve ? "Payroll reversal approved; posting is still required." : "Payroll reversal rejected.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PayrollReversalResultDto>("Reversal changed; reload.",409); }
        catch (DbUpdateException) { return Error<PayrollReversalResultDto>("Reversal review conflict.",409); }
    }

    public async Task<ApiResponse<PayrollReversalResultDto>> PostReversalAsync(Guid reference,
        PostPayrollReversalDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollReversalResultDto>("Tenant administrator permission required.",403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Error<PayrollReversalResultDto>("Reversal version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var (run, original, sourceLines, error) = await ReversalSourceAsync(reference,ct);
            if (run == null || original == null || error != null)
                return Error<PayrollReversalResultDto>(error ?? "Original payroll is invalid.",409);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.SourceType == "PayrollRunReversal" && x.SourceId == run.Id && !x.IsDeleted &&
                (x.State == JournalState.Approved || x.State == JournalState.Posted),ct);
            if (reversal == null) return Error<PayrollReversalResultDto>("Approved reversal not found.",404);
            if (reversal.State == JournalState.Posted)
                return ApiResponse<PayrollReversalResultDto>.SuccessResponse(ReversalResult(run,reversal,true),
                    "Already-posted payroll contra journal returned.");
            if (!Version(reversal.RowVersion,request.RowVersion))
                return Error<PayrollReversalResultDto>("Reversal changed; reload.",409);
            if (reversal.CreatedBy is null or <= 0 || reversal.ApprovedByUserId is null or <= 0 ||
                reversal.CreatedBy == reversal.ApprovedByUserId || reversal.CreatedBy == user.UserId)
                return Error<PayrollReversalResultDto>("Independent approval and a separate posting actor are required.",403);
            if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.Id == reversal.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= reversal.JournalDate && x.EndDate >= reversal.JournalDate,ct))
                return Error<PayrollReversalResultDto>("Reversal accounting period closed.",409);
            var invalid = await ReversalMatchesAsync(run,original,sourceLines,reversal,ct);
            if (invalid != null) return Error<PayrollReversalResultDto>(invalid,409);
            var now = clock.GetUtcNow().UtcDateTime;
            reversal.State = JournalState.Posted;reversal.PostedAt = now;reversal.PostedByUserId = user.UserId;
            reversal.UpdatedAt = now;reversal.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId,UserId = user.UserId,
                Action = "PostPayrollReversal",EntityName = "Journal",EntityId = reversal.Id,
                OldValue = "Approved",NewValue = "Posted",OccurredAt = now });
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<PayrollReversalResultDto>.SuccessResponse(ReversalResult(run,reversal),
                "Independent payroll contra journal posted; original payroll history remains immutable.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PayrollReversalResultDto>("Reversal changed; reload.",409); }
        catch (DbUpdateException) { return Error<PayrollReversalResultDto>("Reversal posting conflict.",409); }
    }
}
