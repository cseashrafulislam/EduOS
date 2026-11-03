using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class FacilityPartsFinanceService
{
    public async Task<ApiResponse<FacilityPartsFinanceRecordDto>> RequestReversalAsync(Guid journalReference,
        FacilityPartsFinanceReversalRequestDto request, CancellationToken ct = default)
    {
        if (!Maker) return Error<FacilityPartsFinanceRecordDto>("Finance maker denied.", 403);
        if (journalReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.JournalDate == default || request.JournalDate > Today ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800)
            return Error<FacilityPartsFinanceRecordDto>("Valid journal, period, date and signed reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(j => j.TenantId == user.TenantId &&
                j.PublicId == journalReference && j.SourceType == "InventoryMovement" && !j.IsDeleted, ct);
            if (original == null || original.SourceId is null or <= 0 || original.State != JournalState.Posted)
                return Error<FacilityPartsFinanceRecordDto>("Posted Facility Finance journal required.", 404);
            var movementRef = await db.InventoryMovements.AsNoTracking().Where(m =>
                m.TenantId == user.TenantId && m.Id == original.SourceId.Value && !m.IsDeleted)
                .Select(m => (Guid?)m.PublicId).FirstOrDefaultAsync(ct);
            var (movement, requestId, campusId) = movementRef.HasValue ? await Source(movementRef.Value, ct) : (null, 0L, 0L);
            if (movement == null || original.CampusId != campusId)
                return Error<FacilityPartsFinanceRecordDto>("Finance source not found.", 404);
            var previousKey = await db.Journals.AsNoTracking().FirstOrDefaultAsync(j => j.TenantId == user.TenantId &&
                j.IdempotencyKey == request.ClientRequestId && !j.IsDeleted, ct);
            if (previousKey != null)
            {
                if (previousKey.SourceType != "FacilityPartsExpenseReversal" ||
                    previousKey.SourceId != original.Id || previousKey.ReversalOfJournalId != original.Id ||
                    previousKey.AccountingPeriodId != request.AccountingPeriodId ||
                    previousKey.JournalDate != request.JournalDate || previousKey.Description != request.Reason.Trim())
                    return Error<FacilityPartsFinanceRecordDto>("Reversal request key belongs to another source.");
                return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                    await Map(original, movement, requestId, campusId, ct), "Existing reversal request returned.");
            }
            if (movement.StateCode != "Posted" || await StockReversed(movement.Id, ct))
                return Error<FacilityPartsFinanceRecordDto>("Stock was already reversed; reconcile before requesting GL contra.");
            if (await db.Journals.AsNoTracking().AnyAsync(j => j.TenantId == user.TenantId &&
                j.SourceType == "FacilityPartsExpenseReversal" && j.SourceId == original.Id && !j.IsDeleted &&
                j.State != JournalState.Rejected && j.State != JournalState.Cancelled, ct))
                return Error<FacilityPartsFinanceRecordDto>("An active Finance reversal already exists.");
            if (!await OpenPeriod(request.AccountingPeriodId, request.JournalDate, ct))
                return Error<FacilityPartsFinanceRecordDto>("Open accounting period required for the reversal.");
            var lines = await db.JournalLines.AsNoTracking().Where(l => l.TenantId == user.TenantId &&
                l.JournalId == original.Id && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync(ct);
            if (lines.Count != 2 || lines[0].DebitAmount <= 0m || lines[0].DebitAmount != lines[1].CreditAmount ||
                lines[0].CreditAmount != 0m || lines[1].DebitAmount != 0m ||
                lines.Any(l => l.CampusId != campusId) ||
                await StockCost(movement.Id, ct) != lines[0].DebitAmount)
                return Error<FacilityPartsFinanceRecordDto>("Original Finance evidence does not match canonical stock cost.");
            var now = Now;
            var reference = Guid.NewGuid();
            var reversal = new Journal {
                TenantId = user.TenantId, PublicId = reference, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "FPR-" + reference.ToString("N").ToUpperInvariant(),
                JournalDate = request.JournalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = campusId, CurrencyCode = "BDT", SourceType = "FacilityPartsExpenseReversal",
                SourceId = original.Id, ReversalOfJournalId = original.Id,
                State = JournalState.Submitted, Description = request.Reason.Trim(),
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(reversal); await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(lines.Select(l => new JournalLine {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = l.LineNo,
                CampusId = campusId, AccountId = l.AccountId,
                DebitAmount = l.CreditAmount, CreditAmount = l.DebitAmount,
                CreatedAt = now, CreatedBy = user.UserId
            }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                await Map(original, movement, requestId, campusId, ct), "Combined GL and stock reversal submitted for independent review.");
        }
        catch (Exception e) when (Conflict(e))
        { logger.LogWarning(e, "Facility Finance reversal conflict");
          return Error<FacilityPartsFinanceRecordDto>("Concurrent Finance reversal; retry same request ID."); }
    }

    public async Task<ApiResponse<FacilityPartsFinanceRecordDto>> ReviewReversalAsync(Guid reversalReference,
        FacilityPartsFinanceReviewDto request, CancellationToken ct = default)
    {
        if (!Reviewer) return Error<FacilityPartsFinanceRecordDto>("Independent reversal reviewer denied.", 403);
        if (reversalReference == Guid.Empty || request == null)
            return Error<FacilityPartsFinanceRecordDto>("Reversal journal and row version required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var reversal = await db.Journals.FirstOrDefaultAsync(j => j.TenantId == user.TenantId &&
                j.PublicId == reversalReference && j.SourceType == "FacilityPartsExpenseReversal" && !j.IsDeleted, ct);
            if (reversal == null || reversal.ReversalOfJournalId is null or <= 0 ||
                reversal.SourceId != reversal.ReversalOfJournalId)
                return Error<FacilityPartsFinanceRecordDto>("Facility Finance reversal not found.", 404);
            var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(j => j.TenantId == user.TenantId &&
                j.Id == reversal.ReversalOfJournalId && j.SourceType == "InventoryMovement" && !j.IsDeleted, ct);
            if (original == null || original.SourceId is null or <= 0)
                return Error<FacilityPartsFinanceRecordDto>("Original posted journal not found.", 404);
            var movementRef = await db.InventoryMovements.AsNoTracking().Where(m => m.TenantId == user.TenantId &&
                m.Id == original.SourceId.Value && !m.IsDeleted).Select(m => (Guid?)m.PublicId).FirstOrDefaultAsync(ct);
            var (movement, requestId, campusId) = movementRef.HasValue ? await Source(movementRef.Value, ct) : (null, 0L, 0L);
            if (movement == null || original.CampusId != campusId || reversal.CampusId != campusId)
                return Error<FacilityPartsFinanceRecordDto>("Facility Finance source not found.", 404);
            if (reversal.State == JournalState.Posted && request.Approve && reversal.ApprovedByUserId == user.UserId)
                return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                    await Map(original, movement, requestId, campusId, ct), "Already reversed.");
            if (reversal.State != JournalState.Submitted || !Version(reversal.RowVersion, request.RowVersion))
                return Error<FacilityPartsFinanceRecordDto>("Submitted reversal version changed.");
            if (reversal.CreatedBy is null or <= 0 || reversal.CreatedBy == user.UserId)
                return Error<FacilityPartsFinanceRecordDto>("Reversal maker and reviewer must differ.", 403);
            if (request.Approve)
            {
                if (original.State != JournalState.Posted || movement.StateCode != "Posted" ||
                    !await OpenPeriod(reversal.AccountingPeriodId, reversal.JournalDate, ct) ||
                    await StockReversed(movement.Id, ct))
                    return Error<FacilityPartsFinanceRecordDto>("Original journal, stock or period changed.");
                var oldLines = await db.JournalLines.AsNoTracking().Where(l => l.TenantId == user.TenantId &&
                    l.JournalId == original.Id && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync(ct);
                var lines = await db.JournalLines.AsNoTracking().Where(l => l.TenantId == user.TenantId &&
                    l.JournalId == reversal.Id && !l.IsDeleted).OrderBy(l => l.LineNo).ToListAsync(ct);
                if (oldLines.Count != 2 || lines.Count != 2 || oldLines[0].DebitAmount <= 0m ||
                    oldLines[0].DebitAmount != oldLines[1].CreditAmount ||
                    await StockCost(movement.Id, ct) != oldLines[0].DebitAmount ||
                    oldLines.Zip(lines, (a, b) => a.LineNo == b.LineNo && a.AccountId == b.AccountId &&
                        a.CampusId == b.CampusId && a.DebitAmount == b.CreditAmount &&
                        a.CreditAmount == b.DebitAmount).Any(same => !same))
                    return Error<FacilityPartsFinanceRecordDto>("Reversal GL is not an exact inverse of the posted original.");
                var (stockReversal, error) = await inventory.ReverseFacilityIssueInTransactionAsync(
                    movement.Id, reversal.PublicId, reversal.Description ?? "Approved facility parts reversal", ct);
                if (stockReversal == null) return Error<FacilityPartsFinanceRecordDto>(error ?? "Stock reversal denied.");
            }
            var now = Now;
            reversal.State = request.Approve ? JournalState.Posted : JournalState.Rejected;
            reversal.ApprovedByUserId = request.Approve ? user.UserId : null;
            reversal.ApprovedAt = request.Approve ? now : null;
            reversal.PostedByUserId = request.Approve ? user.UserId : null;
            reversal.PostedAt = request.Approve ? now : null;
            reversal.UpdatedBy = user.UserId; reversal.UpdatedAt = now;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<FacilityPartsFinanceRecordDto>.SuccessResponse(
                await Map(original, movement, requestId, campusId, ct), request.Approve ?
                "Stock and Finance reversal posted atomically." : "Finance reversal rejected; stock unchanged.");
        }
        catch (Exception e) when (Conflict(e))
        { logger.LogWarning(e, "Facility Finance joint reversal conflict");
          return Error<FacilityPartsFinanceRecordDto>("Concurrent stock/Finance reversal; reload."); }
    }
}
