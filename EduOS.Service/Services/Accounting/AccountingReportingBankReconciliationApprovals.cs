using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private static BankReconciliationApprovalEventDto ApprovalDto(BankReconciliationApprovalEvent e) => new()
    {
        Id = e.Id, ClientRequestId = e.ClientRequestId, BatchId = e.BankReconciliationBatchId,
        ActionCode = e.ActionCode, EvidenceFingerprint = e.EvidenceFingerprint,
        MatchedCount = e.MatchedCount, Reason = e.Reason,
        ActorUserId = e.ActorUserId, EventAt = e.EventAt
    };

    public async Task<ApiResponse<BankReconciliationApprovalStatusDto>> GetBankReconciliationApprovalStatusAsync(
        long batchId, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationApprovalStatusDto>.ErrorResponse("Tenant administrator access required.", 403);
        var batch = await GetBankReconciliationDraftAsync(batchId, ct);
        if (!batch.Success)
            return ApiResponse<BankReconciliationApprovalStatusDto>.ErrorResponse(batch.Message, batch.StatusCode);
        var events = await db.BankReconciliationApprovalEvents.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.BankReconciliationBatchId == batchId)
            .OrderBy(x => x.ActionCode).Take(5).ToListAsync(ct);
        return ApiResponse<BankReconciliationApprovalStatusDto>.SuccessResponse(new()
        {
            BatchId = batchId,
            StateCode = events.Count == 0 ? 1 : events[^1].ActionCode,
            Events = events.Select(ApprovalDto).ToArray()
        });
    }

    // Fingerprint ties approval to the exact immutable statement and explicit, still-live
    // journal-line matches. Every transition rechecks the source before recording evidence.
    private async Task<(string? Error, string Fingerprint, int Count)> ApprovalEvidenceAsync(
        BankReconciliationBatch batch, CancellationToken ct)
    {
        var review = await ReviewSavedBankReconciliationAsync(batch.Id, ct);
        if (!review.Success || review.Data == null)
            return (review.Message, string.Empty, 0);
        var comparison = review.Data.Comparison;
        if (!comparison.IsExactMatch || comparison.ReferenceCollisionCount != 0 ||
            comparison.UnmatchedLedgerCount != 0 || comparison.UnmatchedStatementCount != 0 ||
            comparison.Matches.Count != batch.MovementCount)
            return ("Every statement row and posted bank movement must reconcile exactly.", string.Empty, 0);

        var confirmations = await db.BankReconciliationMatchEvents.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.BankReconciliationBatchId == batch.Id && x.ActionCode == 1)
            .OrderBy(x => x.Id).Take(501).ToListAsync(ct);
        var ids = confirmations.Select(x => x.Id).ToArray();
        var withdrawn = await db.BankReconciliationMatchEvents.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                x.ActionCode == 2 && x.OriginalMatchEventId.HasValue &&
                ids.Contains(x.OriginalMatchEventId.Value))
            .Select(x => x.OriginalMatchEventId!.Value).ToArrayAsync(ct);
        var cancelled = withdrawn.ToHashSet();
        var active = confirmations.Where(x => !cancelled.Contains(x.Id)).ToArray();
        if (active.Length != batch.MovementCount ||
            active.Select(x => x.StatementRowNumber).Distinct().Count() != active.Length ||
            active.Select(x => x.JournalLineId).Distinct().Count() != active.Length)
            return ("Explicit, unique active confirmations are required for every statement row.", string.Empty, 0);

        var live = comparison.Matches.ToDictionary(x => x.StatementRowNumber);
        foreach (var match in active)
        {
            if (!live.TryGetValue(match.StatementRowNumber, out var current) ||
                current.JournalLineId != match.JournalLineId ||
                current.JournalLineVersion != match.JournalLineVersion ||
                current.Date != match.Date || current.Deposit != match.Deposit ||
                current.Withdrawal != match.Withdrawal ||
                (current.Reference ?? string.Empty).Trim() != (match.Reference ?? string.Empty).Trim())
                return ("A posted ledger match changed; re-review before clearance.", string.Empty, 0);
        }

        var buffer = new StringBuilder(batch.SourceFingerprint);
        foreach (var x in active.OrderBy(x => x.StatementRowNumber))
            buffer.Append('|').Append(x.StatementRowNumber).Append(':').Append(x.JournalLineId)
                .Append(':').Append(x.JournalLineVersion).Append(':')
                .Append(x.Deposit.ToString("0.00", CultureInfo.InvariantCulture)).Append(':')
                .Append(x.Withdrawal.ToString("0.00", CultureInfo.InvariantCulture));
        return (null, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buffer.ToString()))), active.Length);
    }

    public async Task<ApiResponse<BankReconciliationApprovalEventDto>> RecordBankReconciliationApprovalAsync(
        BankReconciliationApprovalCommandDto request, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.BatchId <= 0 ||
            request.ActionCode is < 2 or > 5 || string.IsNullOrWhiteSpace(request.BatchRowVersion) ||
            request.BatchRowVersion.Length > 128 ||
            request.Reason?.Length > 250 || request.Reason?.Any(char.IsControl) == true ||
            (request.ActionCode is 4 or 5 && string.IsNullOrWhiteSpace(request.Reason)))
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse("Invalid transition, version or reason.", 400);

        var batchRead = await GetBankReconciliationDraftAsync(request.BatchId, ct);
        if (!batchRead.Success || batchRead.Data == null)
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(batchRead.Message, batchRead.StatusCode);
        if (batchRead.Data.RowVersion != request.BatchRowVersion)
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse("Stale statement version.", 409);
        var reason = request.Reason?.Trim();

        async Task<ApiResponse<BankReconciliationApprovalEventDto>?> Replay()
        {
            var existing = await db.BankReconciliationApprovalEvents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.ClientRequestId == request.ClientRequestId, ct);
            if (existing == null) return null;
            return existing.BankReconciliationBatchId == request.BatchId &&
                existing.ActionCode == request.ActionCode && existing.Reason == reason
                ? ApiResponse<BankReconciliationApprovalEventDto>.SuccessResponse(
                    ApprovalDto(existing), "Existing immutable transition returned.")
                : ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                    "Request key belongs to another transition.", 409);
        }
        var replay = await Replay();
        if (replay != null) return replay;

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            replay = await Replay();
            if (replay != null) return replay;
            var batch = await db.BankReconciliationBatches.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                    x.Id == request.BatchId, ct);
            if (batch == null)
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse("Statement not found.", 404);
            if (Convert.ToBase64String(batch.RowVersion) != request.BatchRowVersion)
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse("Stale statement version.", 409);
            if (await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted && x.IsClosed &&
                x.StartDate <= batch.ToDate && x.EndDate >= batch.FromDate, ct))
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                    "Closed accounting period prevents reconciliation transitions.", 409);

            var history = await db.BankReconciliationApprovalEvents.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                    x.BankReconciliationBatchId == batch.Id)
                .OrderBy(x => x.ActionCode).Take(5).ToListAsync(ct);
            var state = history.Count == 0 ? 1 : history[^1].ActionCode;
            if (request.ActionCode != state + 1)
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                    "Invalid or competing reconciliation transition; refresh history.", 409);
            if (request.ActionCode == 3 &&
                (user.UserId == batch.PreparedByUserId ||
                 user.UserId == history.Single(x => x.ActionCode == 2).ActorUserId ||
                 await db.BankReconciliationMatchEvents.AsNoTracking().AnyAsync(x =>
                     x.TenantId == user.TenantId && !x.IsDeleted &&
                     x.BankReconciliationBatchId == batch.Id && x.ActorUserId == user.UserId, ct)))
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                    "Independent checker must differ from preparer, submitter and match decision actors.", 403);
            if (request.ActionCode == 5 &&
                (user.UserId == history.Single(x => x.ActionCode == 4).ActorUserId ||
                 user.UserId == history.Single(x => x.ActionCode == 3).ActorUserId))
                return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                    "Reversal requires a different checker from the requester and original approver.", 403);

            // A reversal preserves the original approval fingerprint; it never changes GL.
            string fingerprint;
            int count;
            if (request.ActionCode <= 3)
            {
                var evidence = await ApprovalEvidenceAsync(batch, ct);
                if (evidence.Error != null)
                    return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(evidence.Error, 409);
                fingerprint = evidence.Fingerprint;
                count = evidence.Count;
                if (request.ActionCode == 3 &&
                    (history[0].EvidenceFingerprint != fingerprint || history[0].MatchedCount != count))
                    return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                        "Submission evidence is stale; do not approve.", 409);
            }
            else
            {
                var approved = history.Single(x => x.ActionCode == 3);
                fingerprint = approved.EvidenceFingerprint;
                count = approved.MatchedCount;
            }

            var next = new BankReconciliationApprovalEvent
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                BankReconciliationBatchId = batch.Id, ActionCode = request.ActionCode,
                EvidenceFingerprint = fingerprint, MatchedCount = count,
                Reason = reason, ActorUserId = user.UserId, EventAt = DateTime.UtcNow
            };
            db.BankReconciliationApprovalEvents.Add(next);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<BankReconciliationApprovalEventDto>.SuccessResponse(
                ApprovalDto(next), request.ActionCode == 3
                    ? "Independent clearance evidence recorded; posted journals unchanged."
                    : request.ActionCode == 5
                        ? "Compensating clearance reversal recorded; posted journals unchanged."
                        : "Audited reconciliation transition recorded.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                "Concurrent reconciliation transition; refresh and retry.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 3960 })
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return ApiResponse<BankReconciliationApprovalEventDto>.ErrorResponse(
                "Concurrent or duplicate approval; refresh and retry.", 409);
        }
    }
}
