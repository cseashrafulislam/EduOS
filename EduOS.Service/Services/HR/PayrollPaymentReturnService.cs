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
/// Records money actually returned by an employee after a verified salary disbursement.
/// This is not a bank transfer instruction, and does not rewrite the original posted voucher.
/// The maker submits the independent cash/bank receipt reference; another administrator
/// verifies that evidence and posts the exact contra of the original settlement.
/// Full returns only: corrected salary is re-issued as a new, separately approved payment.
/// </summary>
public sealed partial class PayrollPaymentPostingService
{
    private sealed record ReturnSource(PayrollPayment Payment, PayrollEmployee Employee,
        PayrollRun Run, Journal Original, JournalLine[] Lines);

    /// <summary>Independent, history-aware proof required before returned salary frees payable capacity.</summary>
    internal static async Task<bool> HasVerifiedReturnAsync(EduOS.Persistence.Context.EduOSDbContext db,
        long tenant, PayrollPayment payment, CancellationToken ct)
    {
        if (payment.TenantId != tenant || payment.IsDeleted || payment.State != PaymentState.Successful ||
            !payment.JournalId.HasValue || payment.Amount <= 0m) return false;
        var original = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.JournalId && !x.IsDeleted &&
            x.State == JournalState.Posted && x.SourceType == "PayrollPayment" &&
            x.SourceId == payment.Id && !x.ReversalOfJournalId.HasValue, ct);
        if (original == null) return false;
        var returns = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.SourceType == "PayrollPaymentReturn" &&
                x.SourceId == payment.Id && x.State == JournalState.Posted)
            .Take(2).ToArrayAsync(ct);
        if (returns.Length != 1 || returns[0].IsDeleted ||
            returns[0].ReversalOfJournalId != original.Id ||
            returns[0].CurrencyCode != original.CurrencyCode ||
            returns[0].CampusId != original.CampusId ||
            returns[0].JournalDate < original.JournalDate ||
            !returns[0].ApprovedByUserId.HasValue ||
            !returns[0].ApprovedAt.HasValue ||
            !returns[0].PostedByUserId.HasValue ||
            !returns[0].PostedAt.HasValue ||
            !returns[0].CreatedBy.HasValue ||
            returns[0].CreatedBy == returns[0].ApprovedByUserId ||
            string.IsNullOrWhiteSpace(returns[0].ReferenceNumber)) return false;
        if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id != returns[0].Id &&
            x.ReversalOfJournalId == original.Id && x.State == JournalState.Posted, ct))
            return false;
        var journalIds = new[] { original.Id, returns[0].Id };
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && journalIds.Contains(x.JournalId)).ToArrayAsync(ct);
        var source = lines.Where(x => x.JournalId == original.Id).OrderBy(x => x.LineNo).ToArray();
        var contra = lines.Where(x => x.JournalId == returns[0].Id).OrderBy(x => x.LineNo).ToArray();
        if (source.Length != 2 || contra.Length != 2 || source.Any(x => x.IsDeleted))
            return false;
        var payableMatches = source.Where(x =>
            x.Description == "Salary payable settlement" && x.DebitAmount == payment.Amount &&
            x.CreditAmount == 0m).ToArray();
        var disbursementMatches = source.Where(x =>
            x.Description == "Cash/bank payroll disbursement" &&
            x.CreditAmount == payment.Amount && x.DebitAmount == 0m).ToArray();
        if (payableMatches.Length != 1 || disbursementMatches.Length != 1 ||
            payableMatches[0].AccountId == disbursementMatches[0].AccountId ||
            original.CurrencyCode != "BDT" || payment.CurrencyCode != "BDT") return false;
        var salaryPayable = payableMatches[0];
        var employee = await db.PayrollEmployees.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.PayrollEmployeeId && !x.IsDeleted, ct);
        var run = employee == null ? null : await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == employee.PayrollRunId && !x.IsDeleted, ct);
        if (run == null || run.State != PayrollRunState.Posted || !run.JournalId.HasValue ||
            original.CampusId != run.CampusId || original.JournalDate != payment.PaymentDate ||
            salaryPayable.EmployeeId != employee!.EmployeeId ||
            disbursementMatches[0].EmployeeId != employee.EmployeeId)
            return false;
        if (!await db.JournalLines.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
            x.JournalId == run.JournalId && !x.IsDeleted &&
            x.Description == "Payroll net payable" && x.AccountId == salaryPayable.AccountId &&
            x.CreditAmount > 0m && x.DebitAmount == 0m, ct)) return false;
        return
            contra.Where((x, i) => x.IsDeleted || x.LineNo != source[i].LineNo ||
                x.AccountId != source[i].AccountId || x.CampusId != source[i].CampusId ||
                x.EmployeeId != source[i].EmployeeId || x.StudentId != source[i].StudentId ||
                x.AcademicProgramId != source[i].AcademicProgramId || x.FeeHeadId != source[i].FeeHeadId ||
                x.Description != source[i].Description || x.DebitAmount != source[i].CreditAmount ||
                x.CreditAmount != source[i].DebitAmount).Any() == false &&
            source.Sum(x => x.DebitAmount) == payment.Amount &&
            source.Sum(x => x.CreditAmount) == payment.Amount;
    }

    private async Task<ReturnSource?> GetReturnSourceAsync(Guid reference, CancellationToken ct)
    {
        var tenant = user.TenantId;
        var payment = await db.PayrollPayments.FirstOrDefaultAsync(x => x.TenantId == tenant &&
            x.PublicId == reference && !x.IsDeleted, ct);
        if (payment == null || !payment.JournalId.HasValue || payment.CurrencyCode != "BDT" ||
            payment.PaymentMethod is not (PaymentMethodType.Cash or PaymentMethodType.BankTransfer) ||
            !Money(payment.Amount) || payment.Amount <= 0m) return null;
        var employee = await db.PayrollEmployees.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.PayrollEmployeeId && !x.IsDeleted, ct);
        var run = employee == null ? null : await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == employee.PayrollRunId && !x.IsDeleted, ct);
        var original = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.JournalId.Value, ct);
        if (employee == null || run == null || original == null || original.IsDeleted ||
            run.State != PayrollRunState.Posted || !run.JournalId.HasValue ||
            original.SourceType != "PayrollPayment" || original.SourceId != payment.Id ||
            original.State != JournalState.Posted || original.ReversalOfJournalId.HasValue ||
            original.CampusId != run.CampusId || original.JournalDate != payment.PaymentDate ||
            original.CurrencyCode != "BDT") return null;
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.JournalId == original.Id)
            .OrderBy(x => x.LineNo).Take(3).ToArrayAsync(ct);
        if (lines.Length != 2 || lines.Any(x => x.IsDeleted || x.CampusId != run.CampusId ||
            x.EmployeeId != employee.EmployeeId || x.StudentId.HasValue ||
            x.DebitAmount < 0m || x.CreditAmount < 0m) ||
            lines.Count(x => x.DebitAmount == payment.Amount && x.CreditAmount == 0m &&
                x.Description == "Salary payable settlement") != 1 ||
            lines.Count(x => x.CreditAmount == payment.Amount && x.DebitAmount == 0m &&
                x.Description == "Cash/bank payroll disbursement") != 1)
            return null;
        var ids = lines.Select(x => x.AccountId).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            ids.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
            x.AllowsPosting && x.CurrencyCode == "BDT").ToListAsync(ct);
        var payable = lines.Single(x => x.DebitAmount > 0m);
        var asset = lines.Single(x => x.CreditAmount > 0m);
        if (ids.Length != 2 || accounts.Count != 2 ||
            !accounts.Any(x => x.Id == payable.AccountId && x.AccountType == AccountType.Liability) ||
            !accounts.Any(x => x.Id == asset.AccountId && x.AccountType == AccountType.Asset))
            return null;
        return new ReturnSource(payment, employee, run, original, lines);
    }

    private static PayrollPaymentReturnResultDto ReturnResult(ReturnSource source,
        Journal journal, bool replay = false) => new()
    {
        PaymentReference = source.Payment.PublicId, ReturnReference = journal.PublicId,
        JournalNumber = journal.JournalNumber, State = journal.State,
        Amount = source.Payment.Amount, RowVersion = Convert.ToBase64String(journal.RowVersion),
        AlreadyProcessed = replay
    };

    public async Task<ApiResponse<PayrollPaymentReturnResultDto>> RequestReturnAsync(
        Guid paymentReference, RequestPayrollPaymentReturnDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollPaymentReturnResultDto>("Tenant administrator required.", 403);
        if (paymentReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReturnDate == default ||
            request.ReturnDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256)
            return Error<PayrollPaymentReturnResultDto>("Actual return date, receipt evidence, reason, period, key and payment version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await GetReturnSourceAsync(paymentReference, ct);
            if (source == null) return Error<PayrollPaymentReturnResultDto>("Posted payroll payment needs reconciliation.", 409);
            var history = await db.Journals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.SourceType == "PayrollPaymentReturn" &&
                    x.SourceId == source.Payment.Id).OrderByDescending(x => x.Id).Take(101).ToListAsync(ct);
            if (history.Count > 100) return Error<PayrollPaymentReturnResultDto>("Return history needs reconciliation.", 409);
            var replay = history.FirstOrDefault(x => x.IdempotencyKey == request.ClientRequestId);
            if (replay != null)
            {
                if (replay.IsDeleted || replay.ReversalOfJournalId != source.Original.Id ||
                    replay.JournalDate != request.ReturnDate ||
                    replay.AccountingPeriodId != request.AccountingPeriodId ||
                    replay.ReferenceNumber != request.EvidenceReference.Trim() ||
                    replay.Description != request.Reason.Trim())
                    return Error<PayrollPaymentReturnResultDto>("Return key was used with different evidence.", 409);
                return ApiResponse<PayrollPaymentReturnResultDto>.SuccessResponse(
                    ReturnResult(source, replay, true), "Existing return request returned.");
            }
            if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.IdempotencyKey == request.ClientRequestId, ct))
                return Error<PayrollPaymentReturnResultDto>("Return key belongs to another journal.", 409);
            if (source.Payment.State != PaymentState.Successful ||
                !Version(source.Payment.RowVersion, request.RowVersion) ||
                request.ReturnDate < source.Payment.PaymentDate ||
                history.Any(x => !x.IsDeleted && x.State is JournalState.Submitted or JournalState.Approved or JournalState.Posted) ||
                await PayrollPostingHistoryIntegrity.HasPostedReversalAsync(db, tenant, source.Original.Id, ct))
                return Error<PayrollPaymentReturnResultDto>("Payment changed, already returned, or has an active return.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= request.ReturnDate && x.EndDate >= request.ReturnDate, ct);
            if (period == null) return Error<PayrollPaymentReturnResultDto>("Return must be within an open accounting period.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = tenant, PublicId = id, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "PPR-" + id.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReturnDate, AccountingPeriodId = period.Id,
                CampusId = source.Run.CampusId, CurrencyCode = "BDT", State = JournalState.Submitted,
                SourceType = "PayrollPaymentReturn", SourceId = source.Payment.Id,
                ReversalOfJournalId = source.Original.Id,
                ReferenceNumber = request.EvidenceReference.Trim(), Description = request.Reason.Trim(),
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(reversal);
            await db.SaveChangesAsync(ct);
            if (reversal.CreatedBy != user.UserId) reversal.CreatedBy = user.UserId;
            db.JournalLines.AddRange(source.Lines.Select(x => new JournalLine
            {
                TenantId = tenant, JournalId = reversal.Id, LineNo = x.LineNo,
                AccountId = x.AccountId, CampusId = x.CampusId, EmployeeId = x.EmployeeId,
                StudentId = x.StudentId, AcademicProgramId = x.AcademicProgramId, FeeHeadId = x.FeeHeadId,
                DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount,
                Description = x.Description, CreatedAt = now, CreatedBy = user.UserId
            }));
            db.AuditLogs.Add(new AuditLog { TenantId = tenant, UserId = user.UserId,
                Action = "RequestPayrollPaymentReturn", EntityName = "PayrollPayment",
                EntityId = source.Payment.Id, OldValue = source.Original.JournalNumber,
                NewValue = reversal.JournalNumber + "; Receipt: " + request.EvidenceReference.Trim(), OccurredAt = now });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PayrollPaymentReturnResultDto>.SuccessResponse(ReturnResult(source, reversal),
                "Return receipt submitted for independent GL review; original salary remains paid.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PayrollPaymentReturnResultDto>("Payment changed; reload.", 409); }
        catch (DbUpdateException) { return Error<PayrollPaymentReturnResultDto>("Conflicting return; reload.", 409); }
    }

    public async Task<ApiResponse<PayrollPaymentReturnResultDto>> ReviewReturnAsync(
        Guid paymentReference, Guid returnReference, ReviewPayrollPaymentReturnDto request,
        CancellationToken ct = default)
    {
        if (!CanPost) return Error<PayrollPaymentReturnResultDto>("Tenant administrator required.", 403);
        if (paymentReference == Guid.Empty || returnReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256 ||
            (!request.Approve && (string.IsNullOrWhiteSpace(request.RejectionReason) ||
                request.RejectionReason.Trim().Length > 1000)))
            return Error<PayrollPaymentReturnResultDto>("Return reference, version and rejection reason required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await GetReturnSourceAsync(paymentReference, ct);
            if (source == null) return Error<PayrollPaymentReturnResultDto>("Original payroll payment needs reconciliation.", 409);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == returnReference && x.SourceType == "PayrollPaymentReturn" &&
                x.SourceId == source.Payment.Id && !x.IsDeleted, ct);
            if (reversal == null || reversal.ReversalOfJournalId != source.Original.Id)
                return Error<PayrollPaymentReturnResultDto>("Return request not found.", 404);
            if (reversal.State == (request.Approve ? JournalState.Posted : JournalState.Rejected))
                return ApiResponse<PayrollPaymentReturnResultDto>.SuccessResponse(
                    ReturnResult(source, reversal, true), "Existing return decision returned.");
            if (reversal.State != JournalState.Submitted ||
                !Version(reversal.RowVersion, request.RowVersion))
                return Error<PayrollPaymentReturnResultDto>("Return request changed; reload.", 409);
            if (!reversal.CreatedBy.HasValue || reversal.CreatedBy <= 0 ||
                reversal.CreatedBy == user.UserId)
                return Error<PayrollPaymentReturnResultDto>("Return maker and reviewer must be different administrators.", 403);
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Approve)
            {
                if (source.Payment.State != PaymentState.Successful ||
                    reversal.JournalDate < source.Payment.PaymentDate ||
                    string.IsNullOrWhiteSpace(reversal.ReferenceNumber) ||
                    await PayrollPostingHistoryIntegrity.HasPostedReversalAsync(db, tenant, source.Original.Id, ct))
                    return Error<PayrollPaymentReturnResultDto>("Payment already returned or receipt evidence invalid.", 409);
                if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == reversal.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= reversal.JournalDate && x.EndDate >= reversal.JournalDate, ct))
                    return Error<PayrollPaymentReturnResultDto>("Return accounting period has closed.", 409);
                var contra = await db.JournalLines.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.JournalId == reversal.Id)
                    .OrderBy(x => x.LineNo).Take(3).ToArrayAsync(ct);
                if (contra.Length != source.Lines.Length ||
                    contra.Where((x, i) => x.IsDeleted || x.LineNo != source.Lines[i].LineNo ||
                        x.AccountId != source.Lines[i].AccountId ||
                        x.CampusId != source.Lines[i].CampusId ||
                        x.EmployeeId != source.Lines[i].EmployeeId ||
                        x.StudentId != source.Lines[i].StudentId ||
                        x.AcademicProgramId != source.Lines[i].AcademicProgramId ||
                        x.FeeHeadId != source.Lines[i].FeeHeadId ||
                        x.Description != source.Lines[i].Description ||
                        x.DebitAmount != source.Lines[i].CreditAmount ||
                        x.CreditAmount != source.Lines[i].DebitAmount).Any())
                    return Error<PayrollPaymentReturnResultDto>("Contra journal does not exactly reverse salary settlement.", 409);
                reversal.State = JournalState.Posted;
                reversal.ApprovedByUserId = user.UserId; reversal.ApprovedAt = now;
                reversal.PostedByUserId = user.UserId; reversal.PostedAt = now;
                // A posted successful payroll payment is immutable. Its economic balance is
                // corrected by the independently posted contra journal, not by editing its state.
            }
            else reversal.State = JournalState.Rejected;
            reversal.UpdatedBy = user.UserId; reversal.UpdatedAt = now;
            db.AuditLogs.Add(new AuditLog { TenantId = tenant, UserId = user.UserId,
                Action = request.Approve ? "ApprovePayrollPaymentReturn" : "RejectPayrollPaymentReturn",
                EntityName = "PayrollPayment", EntityId = source.Payment.Id,
                OldValue = source.Original.JournalNumber,
                NewValue = request.Approve ? reversal.JournalNumber : request.RejectionReason!.Trim(),
                OccurredAt = now });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PayrollPaymentReturnResultDto>.SuccessResponse(ReturnResult(source, reversal),
                request.Approve ? "Actual salary return posted to balanced GL; original payment preserved."
                    : "Return request rejected; salary remains paid.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PayrollPaymentReturnResultDto>("Return changed; reload.", 409); }
        catch (DbUpdateException) { return Error<PayrollPaymentReturnResultDto>("Return review conflicted with settlement.", 409); }
    }
}
