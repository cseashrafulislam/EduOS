using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class FacilityPartsFinanceService
{
    public async Task<ApiResponse<FacilityPartsFinanceRecordDto>> RequestAsync(Guid movementReference,
        FacilityPartsFinanceRequestDto request, CancellationToken ct = default)
    {
        if (!Maker) return Error<FacilityPartsFinanceRecordDto>("Finance maker denied.", 403);
        if (movementReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.JournalDate == default || request.JournalDate > Today ||
            request.RepairsExpenseAccountId <= 0 || request.InventoryAssetAccountId <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Error<FacilityPartsFinanceRecordDto>("Valid stock source, accounts, period, date and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (movement, requestId, campusId) = await Source(movementReference, ct);
            if (movement == null) return Error<FacilityPartsFinanceRecordDto>("Facility movement not found.", 404);
            var prior = await db.Journals.AsNoTracking().FirstOrDefaultAsync(j =>
                j.TenantId == user.TenantId && j.IdempotencyKey == request.ClientRequestId && !j.IsDeleted, ct);
            if (prior != null)
            {
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                    x.JournalId == prior.Id && !x.IsDeleted).ToListAsync(ct);
                if (prior.SourceType != "InventoryMovement" || prior.SourceId != movement.Id ||
                    prior.CampusId != campusId || prior.AccountingPeriodId != request.AccountingPeriodId ||
                    prior.JournalDate != request.JournalDate || prior.Description != request.Reason.Trim() ||
                    lines.Count != 2 || !lines.Any(x => x.LineNo == 1 &&
                        x.AccountId == request.RepairsExpenseAccountId && x.DebitAmount > 0m && x.CreditAmount == 0m) ||
                    !lines.Any(x => x.LineNo == 2 && x.AccountId == request.InventoryAssetAccountId &&
                        x.CreditAmount > 0m && x.DebitAmount == 0m))
                    return Error<FacilityPartsFinanceRecordDto>("Request key already belongs to a different journal.");
                return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                    await Map(prior, movement, requestId, campusId, ct), "Existing Finance request returned.");
            }
            if (movement.StateCode != "Posted" || await StockReversed(movement.Id, ct))
                return Error<FacilityPartsFinanceRecordDto>("Only posted and unreversed facility stock can be expensed.");
            if (await db.Journals.AsNoTracking().AnyAsync(j => j.TenantId == user.TenantId &&
                j.SourceType == "InventoryMovement" && j.SourceId == movement.Id && !j.IsDeleted &&
                j.State != JournalState.Rejected && j.State != JournalState.Cancelled, ct))
                return Error<FacilityPartsFinanceRecordDto>("Active Finance recognition already exists for this stock issue.");
            if (!await OpenPeriod(request.AccountingPeriodId, request.JournalDate, ct) ||
                !await AccountsValid(request.RepairsExpenseAccountId, request.InventoryAssetAccountId, ct))
                return Error<FacilityPartsFinanceRecordDto>("Open period and distinct active BDT expense/stock asset accounts required.");
            var amount = await StockCost(movement.Id, ct);
            if (!amount.HasValue) return Error<FacilityPartsFinanceRecordDto>("Posted stock value is zero or invalid; reconcile Inventory valuation first.");
            var now = Now;
            var journalRef = Guid.NewGuid();
            var journal = new Journal {
                TenantId = user.TenantId, PublicId = journalRef, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FPI-" + journalRef.ToString("N").ToUpperInvariant(),
                JournalDate = request.JournalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = campusId, CurrencyCode = "BDT", SourceType = "InventoryMovement", SourceId = movement.Id,
                State = JournalState.Submitted, Description = request.Reason.Trim(),
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(journal); await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(new JournalLine {
                TenantId = user.TenantId, JournalId = journal.Id, LineNo = 1,
                AccountId = request.RepairsExpenseAccountId, CampusId = campusId,
                DebitAmount = amount.Value, CreatedAt = now, CreatedBy = user.UserId
            }, new JournalLine {
                TenantId = user.TenantId, JournalId = journal.Id, LineNo = 2,
                AccountId = request.InventoryAssetAccountId, CampusId = campusId,
                CreditAmount = amount.Value, CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                await Map(journal, movement, requestId, campusId, ct), "Facility parts expense submitted for independent Finance review.");
        }
        catch (Exception e) when (Conflict(e))
        { logger.LogWarning(e, "Facility parts Finance recognition conflict");
          return Error<FacilityPartsFinanceRecordDto>("Concurrent Finance request; retry with the same request ID."); }
    }

    public async Task<ApiResponse<FacilityPartsFinanceRecordDto>> ReviewAsync(Guid journalReference,
        FacilityPartsFinanceReviewDto request, CancellationToken ct = default)
    {
        if (!Reviewer) return Error<FacilityPartsFinanceRecordDto>("Independent Finance reviewer denied.", 403);
        if (journalReference == Guid.Empty || request == null)
            return Error<FacilityPartsFinanceRecordDto>("Journal and row version required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var journal = await db.Journals.FirstOrDefaultAsync(j => j.TenantId == user.TenantId &&
                j.PublicId == journalReference && j.SourceType == "InventoryMovement" && !j.IsDeleted, ct);
            if (journal == null || journal.SourceId is null or <= 0)
                return Error<FacilityPartsFinanceRecordDto>("Facility Finance journal not found.", 404);
            var movementRef = await db.InventoryMovements.AsNoTracking().Where(m =>
                m.TenantId == user.TenantId && m.Id == journal.SourceId.Value && !m.IsDeleted)
                .Select(m => (Guid?)m.PublicId).FirstOrDefaultAsync(ct);
            var (movement, requestId, campusId) = movementRef.HasValue ? await Source(movementRef.Value, ct) : (null, 0L, 0L);
            if (movement == null || journal.CampusId != campusId)
                return Error<FacilityPartsFinanceRecordDto>("Facility Finance source not found.", 404);
            if (journal.State == JournalState.Posted && request.Approve && journal.ApprovedByUserId == user.UserId)
                return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                    await Map(journal, movement, requestId, campusId, ct), "Already reviewed and posted.");
            if (journal.State != JournalState.Submitted || !Version(journal.RowVersion, request.RowVersion))
                return Error<FacilityPartsFinanceRecordDto>("Submitted journal version changed.");
            if (journal.CreatedBy is null or <= 0 || journal.CreatedBy == user.UserId)
                return Error<FacilityPartsFinanceRecordDto>("Maker and reviewer must be distinct users.", 403);
            if (request.Approve)
            {
                if (movement.StateCode != "Posted" || await StockReversed(movement.Id, ct) ||
                    !await OpenPeriod(journal.AccountingPeriodId, journal.JournalDate, ct))
                    return Error<FacilityPartsFinanceRecordDto>("Stock or accounting period changed; cannot post.");
                var lines = await db.JournalLines.AsNoTracking().Where(l => l.TenantId == user.TenantId &&
                    l.JournalId == journal.Id && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync(ct);
                if (lines.Count != 2 || lines[0].LineNo != 1 || lines[1].LineNo != 2 ||
                    lines[0].DebitAmount <= 0m || lines[0].CreditAmount != 0m ||
                    lines[1].CreditAmount != lines[0].DebitAmount || lines[1].DebitAmount != 0m ||
                    lines.Any(l => l.CampusId != campusId) ||
                    !await AccountsValid(lines[0].AccountId, lines[1].AccountId, ct) ||
                    await StockCost(movement.Id, ct) != lines[0].DebitAmount)
                    return Error<FacilityPartsFinanceRecordDto>("Finance lines no longer reconcile to posted stock cost.");
            }
            var now = Now;
            journal.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            journal.ApprovedByUserId = request.Approve ? user.UserId : null;
            journal.ApprovedAt = request.Approve ? now : null;
            journal.PostedByUserId = request.Approve ? user.UserId : null;
            journal.PostedAt = request.Approve ? now : null;
            journal.UpdatedBy = user.UserId; journal.UpdatedAt = now;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                await Map(journal, movement, requestId, campusId, ct), request.Approve ?
                "Facility stock expense posted to canonical Finance GL." : "Finance request rejected.");
        }
        catch (Exception e) when (Conflict(e))
        { logger.LogWarning(e, "Facility Finance review conflict");
          return Error<FacilityPartsFinanceRecordDto>("Finance approval conflict; reload."); }
    }
}
