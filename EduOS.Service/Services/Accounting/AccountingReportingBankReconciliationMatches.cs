using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    private static BankReconciliationMatchEventDto MatchDto(BankReconciliationMatchEvent e, bool withdrawn = false) => new()
    {
        Id = e.Id, BatchId = e.BankReconciliationBatchId, ClientRequestId = e.ClientRequestId,
        ActionCode = e.ActionCode, OriginalMatchEventId = e.OriginalMatchEventId,
        StatementRowNumber = e.StatementRowNumber, JournalLineId = e.JournalLineId,
        JournalLineVersion = e.JournalLineVersion, Date = e.Date, Reference = e.Reference,
        Deposit = e.Deposit, Withdrawal = e.Withdrawal, Reason = e.Reason,
        ActorUserId = e.ActorUserId, EventAt = e.EventAt, IsWithdrawn = withdrawn
    };

    public async Task<ApiResponse<PagedResult<BankReconciliationMatchEventDto>>> GetBankReconciliationMatchEventsAsync(
        long batchId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<PagedResult<BankReconciliationMatchEventDto>>.ErrorResponse("Tenant administrator access required.", 403);
        if (batchId <= 0 || page < 1 || page > 10000 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > 1000000)
            return ApiResponse<PagedResult<BankReconciliationMatchEventDto>>.ErrorResponse("Invalid batch or bounded page.", 400);
        var draft = await GetBankReconciliationDraftAsync(batchId, ct);
        if (!draft.Success)
            return ApiResponse<PagedResult<BankReconciliationMatchEventDto>>.ErrorResponse(draft.Message, draft.StatusCode);

        var query = db.BankReconciliationMatchEvents.AsNoTracking().Where(e =>
            e.TenantId == user.TenantId && !e.IsDeleted && e.BankReconciliationBatchId == batchId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(ct);
        var matchIds = rows.Where(e => e.ActionCode == 1).Select(e => e.Id).ToArray();
        var withdrawn = await db.BankReconciliationMatchEvents.AsNoTracking()
            .Where(e => e.TenantId == user.TenantId && !e.IsDeleted &&
                e.ActionCode == 2 && e.OriginalMatchEventId.HasValue &&
                matchIds.Contains(e.OriginalMatchEventId.Value))
            .Select(e => e.OriginalMatchEventId!.Value).ToArrayAsync(ct);
        var cancelled = withdrawn.ToHashSet();
        return ApiResponse<PagedResult<BankReconciliationMatchEventDto>>.SuccessResponse(
            new PagedResult<BankReconciliationMatchEventDto>
            {
                Items = rows.Select(e => MatchDto(e, e.ActionCode == 1 && cancelled.Contains(e.Id))).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    // Explicit, append-only draft matching and compensating withdrawal. No journal updates,
    // clearance or approval. The serializable transaction protects active-line uniqueness.
    public async Task<ApiResponse<BankReconciliationMatchEventDto>> RecordBankReconciliationMatchEventAsync(
        BankReconciliationMatchCommandDto request, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (request == null || request.BatchId <= 0 || request.ClientRequestId == Guid.Empty ||
            request.ActionCode is not (1 or 2) ||
            (request.ActionCode == 1 && (request.StatementRowNumber is < 1 or > 500 ||
                request.JournalLineId <= 0 || request.OriginalMatchEventId.HasValue)) ||
            (request.ActionCode == 2 && (request.OriginalMatchEventId is null or <= 0 ||
                string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 250)) ||
            request.Reason?.Any(char.IsControl) == true)
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse("Invalid match command, request key or withdrawal reason.", 400);

        var draft = await GetBankReconciliationDraftAsync(request.BatchId, ct);
        if (!draft.Success || draft.Data == null)
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(draft.Message, draft.StatusCode);
        if (draft.Data.StateCode != 1 || draft.Data.RowVersion != request.BatchRowVersion)
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse("Stale statement version or invalid draft state.", 409);

        async Task<ApiResponse<BankReconciliationMatchEventDto>?> Replay()
        {
            var existing = await db.BankReconciliationMatchEvents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.ClientRequestId == request.ClientRequestId, ct);
            if (existing == null) return null;
            bool same = existing.BankReconciliationBatchId == request.BatchId &&
                existing.ActionCode == request.ActionCode &&
                (request.ActionCode == 1
                    ? existing.StatementRowNumber == request.StatementRowNumber &&
                      existing.JournalLineId == request.JournalLineId &&
                      existing.JournalLineVersion == request.JournalLineVersion
                    : existing.OriginalMatchEventId == request.OriginalMatchEventId &&
                      existing.Reason == request.Reason?.Trim());
            return same
                ? ApiResponse<BankReconciliationMatchEventDto>.SuccessResponse(MatchDto(existing),
                    "Existing immutable decision returned.")
                : ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                    "Request key already used for another decision.", 409);
        }
        var replay = await Replay();
        if (replay != null) return replay;

        // A posted bank line may never be edited. New journal activity is rechecked
        // under the same transaction before recording a matching decision.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            replay = await Replay();
            if (replay != null) return replay;
            if (await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted && x.IsClosed &&
                x.StartDate <= draft.Data.ToDate && x.EndDate >= draft.Data.FromDate, ct))
                return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                    "Closed accounting period blocks matching changes.", 409);

            BankReconciliationMatchEvent next;
            if (request.ActionCode == 1)
            {
                var live = await ReviewSavedBankReconciliationAsync(request.BatchId, ct);
                if (!live.Success || live.Data == null)
                    return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(live.Message, live.StatusCode);
                var match = live.Data.Comparison.Matches.SingleOrDefault(x =>
                    x.StatementRowNumber == request.StatementRowNumber &&
                    x.JournalLineId == request.JournalLineId);
                if (match == null || match.JournalLineVersion != request.JournalLineVersion ||
                    (db.Database.IsRelational() && string.IsNullOrEmpty(match.JournalLineVersion)))
                    return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                        "No current unique exact match or stale ledger-line version.", 409);

                // Both queries are indexed by tenant/row and tenant/ledger line. Under
                // SQL SERIALIZABLE they prevent concurrent active matches in any batch.
                var candidates = await db.BankReconciliationMatchEvents.AsNoTracking()
                    .Where(e => e.TenantId == user.TenantId && !e.IsDeleted &&
                        e.ActionCode == 1 &&
                        (e.JournalLineId == request.JournalLineId ||
                         (e.BankReconciliationBatchId == request.BatchId &&
                          e.StatementRowNumber == request.StatementRowNumber)))
                    .Select(e => e.Id).ToArrayAsync(ct);
                if (candidates.Length > 0)
                {
                    var withdrawals = await db.BankReconciliationMatchEvents.AsNoTracking()
                        .Where(e => e.TenantId == user.TenantId && !e.IsDeleted &&
                            e.ActionCode == 2 && e.OriginalMatchEventId.HasValue &&
                            candidates.Contains(e.OriginalMatchEventId.Value))
                        .Select(e => e.OriginalMatchEventId!.Value).ToArrayAsync(ct);
                    if (candidates.Except(withdrawals).Any())
                        return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                            "Statement row or posted journal line already has an active draft match.", 409);
                }
                next = new BankReconciliationMatchEvent
                {
                    TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                    BankReconciliationBatchId = request.BatchId, ActionCode = 1,
                    StatementRowNumber = match.StatementRowNumber, JournalLineId = match.JournalLineId,
                    JournalLineVersion = match.JournalLineVersion, Date = match.Date,
                    Reference = match.Reference?.Trim(), Deposit = match.Deposit,
                    Withdrawal = match.Withdrawal, ActorUserId = user.UserId, EventAt = DateTime.UtcNow
                };
            }
            else
            {
                var original = await db.BankReconciliationMatchEvents.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.TenantId == user.TenantId && !e.IsDeleted &&
                        e.Id == request.OriginalMatchEventId && e.BankReconciliationBatchId == request.BatchId &&
                        e.ActionCode == 1, ct);
                if (original == null)
                    return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse("Match event not found.", 404);
                if (await db.BankReconciliationMatchEvents.AsNoTracking().AnyAsync(e =>
                    e.TenantId == user.TenantId && !e.IsDeleted && e.ActionCode == 2 &&
                    e.OriginalMatchEventId == original.Id, ct))
                    return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                        "Match was already withdrawn.", 409);
                next = new BankReconciliationMatchEvent
                {
                    TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                    BankReconciliationBatchId = request.BatchId, ActionCode = 2,
                    OriginalMatchEventId = original.Id,
                    StatementRowNumber = original.StatementRowNumber,
                    JournalLineId = original.JournalLineId,
                    JournalLineVersion = original.JournalLineVersion, Date = original.Date,
                    Reference = original.Reference, Deposit = original.Deposit,
                    Withdrawal = original.Withdrawal, Reason = request.Reason!.Trim(),
                    ActorUserId = user.UserId, EventAt = DateTime.UtcNow
                };
            }
            db.BankReconciliationMatchEvents.Add(next);
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<BankReconciliationMatchEventDto>.SuccessResponse(
                MatchDto(next), "Draft decision recorded; not approved or cleared.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                "Concurrent reconciliation change; reload and retry.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 3960 })
        {
            if (tx != null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return ApiResponse<BankReconciliationMatchEventDto>.ErrorResponse(
                "Concurrent decision conflict; reload and retry with a new request key.", 409);
        }
    }
}
