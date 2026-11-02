using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetAccountingService
{
    private sealed record ReversibleEvent(object Row, AssetBook Book, long JournalId,
        long? ReversalJournalId, DateOnly Date, byte[] RowVersion, string State);

    private async Task<ReversibleEvent?> EventSourceAsync(string kind, long id, CancellationToken ct)
    {
        if (kind == "Depreciation")
        {
            var r = await db.AssetDepreciationRuns.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (r?.JournalId is null) return null;
            var line = await db.AssetDepreciationLines.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.AssetDepreciationRunId == id && !x.IsDeleted, ct);
            var b = line == null ? null : await db.AssetBooks.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == line.AssetBookId && !x.IsDeleted, ct);
            return b == null ? null : new(r, b, r.JournalId.Value,
                r.ReversalJournalId, r.PeriodEnd, r.RowVersion, r.StateCode);
        }
        if (kind == "Impairment")
        {
            var r = await db.AssetImpairments.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (r?.JournalId is null) return null;
            var b = await db.AssetBooks.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == r.AssetBookId && !x.IsDeleted, ct);
            return b == null ? null : new(r, b, r.JournalId.Value,
                r.ReversalJournalId, r.EffectiveDate, r.RowVersion, r.StateCode);
        }
        if (kind == "Disposal")
        {
            var r = await db.AssetDisposals.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (r?.JournalId is null) return null;
            var b = await db.AssetBooks.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == r.AssetBookId && !x.IsDeleted, ct);
            return b == null ? null : new(r, b, r.JournalId.Value,
                r.ReversalJournalId, r.DisposalDate, r.RowVersion, r.StateCode);
        }
        return null;
    }

    private async Task<bool> LaterPostingAsync(ReversibleEvent source, string kind, long id,
        CancellationToken ct)
    {
        var bookId = source.Book.Id;
        var postedAt = source.Row switch
        {
            AssetDepreciationRun r => r.PostedAt ?? r.ApprovedAt,
            AssetImpairment r => r.ApprovedAt,
            AssetDisposal r => r.ApprovedAt,
            _ => null
        } ?? DateTime.MinValue;
        return await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(r =>
                r.TenantId == user.TenantId && r.StateCode == "Posted" && !r.IsDeleted &&
                (kind != "Depreciation" || r.Id != id) &&
                db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId &&
                    l.AssetDepreciationRunId == r.Id && l.AssetBookId == bookId &&
                    !l.IsDeleted && (l.PeriodEnd > source.Date ||
                        (l.PeriodEnd == source.Date && (r.PostedAt == null || r.PostedAt > postedAt)))), ct) ||
            await db.AssetImpairments.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetBookId == bookId &&
                x.StateCode == "Posted" && !x.IsDeleted &&
                (kind != "Impairment" || x.Id != id) &&
                (x.EffectiveDate > source.Date ||
                    (x.EffectiveDate == source.Date && (x.ApprovedAt == null || x.ApprovedAt > postedAt))), ct) ||
            await db.AssetDisposals.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetBookId == bookId &&
                x.StateCode == "Posted" && !x.IsDeleted &&
                (kind != "Disposal" || x.Id != id) &&
                (x.DisposalDate > source.Date ||
                    (x.DisposalDate == source.Date && (x.ApprovedAt == null || x.ApprovedAt > postedAt))), ct);
    }

    private async Task<AssetAccountingEventDto> ReversalMapAsync(
        string kind, long id, Journal? reversal, CancellationToken ct)
    {
        var source = await EventSourceAsync(kind, id, ct);
        if (source == null) return new AssetAccountingEventDto();
        Guid? originalRef = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.Id == source.JournalId).Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct);
        var result = source.Row switch
        {
            AssetDepreciationRun r => Event(kind, id, r.ClientRequestId, r.StateCode,
                r.PeriodEnd, r.TotalAmount, originalRef, reversal?.PublicId,
                r.CreatedBy, r.ApprovedByUserId, r.RowVersion),
            AssetImpairment r => Event(kind, id, r.ClientRequestId, r.StateCode,
                r.EffectiveDate, r.Amount, originalRef, reversal?.PublicId,
                r.CreatedBy, r.ApprovedByUserId, r.RowVersion),
            AssetDisposal r => Event(kind, id, r.ClientRequestId, r.StateCode,
                r.DisposalDate, r.Proceeds, originalRef, reversal?.PublicId,
                r.CreatedBy, r.ApprovedByUserId, r.RowVersion),
            _ => new AssetAccountingEventDto()
        };
        if (reversal?.State == JournalState.Submitted)
        {
            result.PendingReversalReference = reversal.PublicId;
            result.PendingReversalRowVersion = Convert.ToBase64String(reversal.RowVersion);
        }
        return result;
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> RequestEventReversalAsync(
        string kind, long eventId, AssetReversalRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetAccountingEventDto>("Asset Finance reversal maker denied.", 403);
        if (kind is not ("Depreciation" or "Impairment" or "Disposal") || eventId <= 0 ||
            request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || !ReasonValid(request.Reason))
            return Conflict<AssetAccountingEventDto>("Valid event, reversal key, period and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Resolve the idempotency key before reading a source event: an approved
            // depreciation/disposal correction intentionally tombstones its source line.
            // The immutable original journal is the authoritative source/campus link.
            var replay = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (!await CampusAllowedAsync(replay.CampusId, ct))
                    return Conflict<AssetAccountingEventDto>("Asset event not found.", 404);
                var original = replay.ReversalOfJournalId.HasValue
                    ? await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == user.TenantId && x.Id == replay.ReversalOfJournalId.Value &&
                        x.SourceType == "Asset" + kind && x.SourceId == eventId &&
                        x.CampusId == replay.CampusId && x.State == JournalState.Posted &&
                        !x.IsDeleted, ct)
                    : null;
                if (replay.IsDeleted || replay.CreatedBy != user.UserId ||
                    replay.SourceType != "Asset" + kind + "Reversal" ||
                    replay.SourceId != eventId || original == null ||
                    replay.AccountingPeriodId != request.AccountingPeriodId ||
                    replay.JournalDate != request.ReversalDate ||
                    replay.Description != request.Reason.Trim() ||
                    replay.CurrencyCode != "BDT" ||
                    replay.State is not (JournalState.Submitted or JournalState.Posted or JournalState.Rejected))
                    return Conflict<AssetAccountingEventDto>("Reversal key already belongs to another event.");
                var sourceForReadback = await EventSourceAsync(kind, eventId, ct);
                // A posted disposal/depreciation can no longer be projected through
                // its tombstoned source. Return only verified journal identity/status,
                // never a fabricated asset valuation or a second posting.
                var result = sourceForReadback == null
                    ? new AssetAccountingEventDto
                    {
                        Kind = kind, Id = eventId,
                        ClientRequestId = original.IdempotencyKey ?? Guid.Empty,
                        State = replay.State == JournalState.Posted ? "Reversed" : "Posted",
                        EffectiveDate = original.JournalDate,
                        JournalReference = original.PublicId,
                        ReversalJournalReference = replay.PublicId,
                        MakerId = replay.CreatedBy, ReviewerId = replay.ApprovedByUserId,
                        PendingReversalReference = replay.State == JournalState.Submitted
                            ? replay.PublicId : null,
                        PendingReversalRowVersion = replay.State == JournalState.Submitted
                            ? Convert.ToBase64String(replay.RowVersion) : null
                    }
                    : await ReversalMapAsync(kind, eventId, replay, ct);
                return ApiResponse<AssetAccountingEventDto>.SuccessResponse(result,
                    "Existing Asset Finance reversal returned.");
            }
            var source = await EventSourceAsync(kind, eventId, ct);
            if (source == null || !await CampusAllowedAsync(source.Book.CampusId, ct))
                return Conflict<AssetAccountingEventDto>("Asset event not found.", 404);
            if (source.State != "Posted" || source.ReversalJournalId.HasValue ||
                !Version(source.RowVersion, request.OriginalRowVersion) ||
                request.ReversalDate < source.Date || request.ReversalDate > Today ||
                !await OpenPeriodAsync(request.AccountingPeriodId, request.ReversalDate, ct) ||
                await LaterPostingAsync(source, kind, eventId, ct) ||
                await CompetingAsync(source.Book.Id, kind, eventId, ct))
                return Conflict<AssetAccountingEventDto>("Only the latest posted event with an open reversal period can be corrected.");
            var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == source.JournalId &&
                x.State == JournalState.Posted && !x.IsDeleted, ct);
            if (original == null) return Conflict<AssetAccountingEventDto>("Original GL posting missing.");
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.ReversalOfJournalId == original.Id &&
                (x.State == JournalState.Posted || (x.State == JournalState.Submitted && !x.IsDeleted)), ct))
                return Conflict<AssetAccountingEventDto>("Already reversed or reversal pending.");
            var oldLines = await db.JournalLines.AsNoTracking().Where(x =>
                x.TenantId == user.TenantId && x.JournalId == original.Id && !x.IsDeleted)
                .OrderBy(x => x.LineNo).ToListAsync(ct);
            if (oldLines.Count < 2 || oldLines.Sum(x => x.DebitAmount) != oldLines.Sum(x => x.CreditAmount))
                return Conflict<AssetAccountingEventDto>("Original GL does not reconcile.");
            var id = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = id,
                IdempotencyKey = request.ClientRequestId,
                JournalNumber = "AR-" + id.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = source.Book.CampusId, CurrencyCode = "BDT",
                SourceType = "Asset" + kind + "Reversal", SourceId = eventId,
                ReversalOfJournalId = original.Id, State = JournalState.Submitted,
                Description = request.Reason.Trim(), CreatedBy = user.UserId, CreatedAt = Now
            };
            db.Journals.Add(reversal); await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(oldLines.Select(l => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = l.LineNo,
                CampusId = l.CampusId, AccountId = l.AccountId, DebitAmount = l.CreditAmount,
                CreditAmount = l.DebitAmount, Description = "Exact Asset Finance contra",
                CreatedBy = user.UserId, CreatedAt = Now
            }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(
                await ReversalMapAsync(kind, eventId, reversal, ct),
                "Independent GL reversal submitted. No bank transfer or stock movement occurs.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent reversal."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset reversal SQL conflict"); return Conflict<AssetAccountingEventDto>("Reversal SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Reversal SQL contention."); }
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> ReviewEventReversalAsync(
        string kind, long eventId, Guid reversalReference, AssetReviewRequest request,
        CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetAccountingEventDto>("Independent reversal reviewer denied.", 403);
        if (request == null || !ReasonValid(request.Reason) || reversalReference == Guid.Empty)
            return Conflict<AssetAccountingEventDto>("Reversal version and review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await EventSourceAsync(kind, eventId, ct);
            if (source == null || !await CampusAllowedAsync(source.Book.CampusId, ct))
                return Conflict<AssetAccountingEventDto>("Asset event not found.", 404);
            var reversal = await db.Journals.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reversalReference &&
                x.SourceType == "Asset" + kind + "Reversal" && x.SourceId == eventId &&
                x.ReversalOfJournalId == source.JournalId && !x.IsDeleted, ct);
            if (reversal == null) return Conflict<AssetAccountingEventDto>("Reversal not found.", 404);
            if (reversal.State != JournalState.Submitted ||
                !Version(reversal.RowVersion, request.RowVersion) ||
                reversal.CreatedBy is null or <= 0 || reversal.CreatedBy == user.UserId)
                return Conflict<AssetAccountingEventDto>("Reversal changed or same maker/reviewer prohibited.");
            if (!request.Approve)
            {
                reversal.State = JournalState.Rejected;
            }
            else
            {
                if (source.State != "Posted" || source.ReversalJournalId.HasValue ||
                    reversal.JournalDate < source.Date || reversal.JournalDate > Today ||
                    !await OpenPeriodAsync(reversal.AccountingPeriodId, reversal.JournalDate, ct) ||
                    await LaterPostingAsync(source, kind, eventId, ct) ||
                    await CompetingAsync(source.Book.Id, kind, eventId, ct))
                    return Conflict<AssetAccountingEventDto>("Asset changed since reversal request.");
                var original = await db.Journals.AsNoTracking().FirstAsync(x =>
                    x.TenantId == user.TenantId && x.Id == source.JournalId, ct);
                var oldLines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == original.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                var newLines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == reversal.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                if (original.State != JournalState.Posted || oldLines.Count < 2 ||
                    oldLines.Count != newLines.Count ||
                    oldLines.Where((l, i) => l.LineNo != newLines[i].LineNo ||
                        l.AccountId != newLines[i].AccountId ||
                        l.CampusId != newLines[i].CampusId ||
                        l.DebitAmount != newLines[i].CreditAmount ||
                        l.CreditAmount != newLines[i].DebitAmount).Any() ||
                    oldLines.Sum(x => x.DebitAmount) != oldLines.Sum(x => x.CreditAmount))
                    return Conflict<AssetAccountingEventDto>("Contra differs from original GL.");
                reversal.State = JournalState.Posted;
                reversal.PostedAt = Now; reversal.PostedByUserId = user.UserId;
                if (source.Row is AssetDepreciationRun dep)
                {
                    dep.StateCode = "Reversed"; dep.ReversalJournalId = reversal.Id;
                    var line = await db.AssetDepreciationLines.FirstAsync(x =>
                        x.TenantId == user.TenantId && x.AssetDepreciationRunId == dep.Id && !x.IsDeleted, ct);
                    line.IsDeleted = true; line.DeletedAt = Now; line.DeletedBy = user.UserId;
                }
                else if (source.Row is AssetImpairment imp)
                {
                    imp.StateCode = "Reversed"; imp.ReversalJournalId = reversal.Id;
                }
                else if (source.Row is AssetDisposal disp)
                {
                    disp.StateCode = "Reversed"; disp.ReversalJournalId = reversal.Id;
                    disp.IsDeleted = true; disp.DeletedAt = Now; disp.DeletedBy = user.UserId;
                    source.Book.IsActive = true; source.Book.UpdatedAt = Now;
                    source.Book.UpdatedBy = user.UserId;
                    var asset = await db.Assets.FirstAsync(x => x.TenantId == user.TenantId &&
                        x.Id == source.Book.AssetId, ct);
                    if (asset.StateCode != "Disposed" || asset.IsActive)
                        return Conflict<AssetAccountingEventDto>("Disposed asset custody changed.");
                    asset.StateCode = "Available"; asset.IsActive = true;
                    asset.UpdatedAt = Now; asset.UpdatedBy = user.UserId;
                }
                else return Conflict<AssetAccountingEventDto>("Unknown event type.");
            }
            reversal.ApprovedByUserId = user.UserId; reversal.ApprovedAt = Now;
            reversal.UpdatedAt = Now; reversal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            // For tombstoned depreciation/disposal source, return the unchanged
            // source reference from the committed review rather than query-filtering it.
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(new AssetAccountingEventDto
            {
                Kind = kind, Id = eventId, State = request.Approve ? "Reversed" : "Posted",
                EffectiveDate = source.Date, ReversalJournalReference = reversal.PublicId,
                RowVersion = Convert.ToBase64String(source.RowVersion)
            }, "Asset Finance GL reversal reviewed; source audit retained.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent reversal approval."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset GL contra conflict"); return Conflict<AssetAccountingEventDto>("GL contra rolled back."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Reversal SQL contention."); }
    }
}
