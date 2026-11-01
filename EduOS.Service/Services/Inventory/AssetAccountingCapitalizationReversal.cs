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
    private const string CapReversalSource = "AssetCapitalizationReversal";

    private async Task<bool> UnwindEligibleAsync(AssetCapitalizationCommand c, CancellationToken ct)
    {
        if (c.StateCode != "Posted" || c.AssetBookId is null or <= 0 ||
            c.StockMovementId is null or <= 0 || c.JournalId is null or <= 0)
            return false;
        var book = await db.AssetBooks.AsNoTracking().FirstOrDefaultAsync(b =>
            b.TenantId == user.TenantId && b.Id == c.AssetBookId && !b.IsDeleted, ct);
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == user.TenantId && a.Id == c.AssetId && !a.IsDeleted, ct);
        if (book == null || !book.IsActive || book.AssetId != c.AssetId || book.CampusId != c.CampusId ||
            asset == null || !asset.IsActive || asset.StateCode != "Available" ||
            asset.CampusId != c.CampusId ||
            await db.AssetAssignments.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AssetId == c.AssetId && !x.IsDeleted && x.ReturnedOn == null, ct))
            return false;
        // A capitalized unit with any live or posted valuation/custody flow must first
        // complete that workflow. Never erase a posted GL line or a prior source audit.
        return !await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(r =>
                r.TenantId == user.TenantId && !r.IsDeleted &&
                (r.StateCode == "Submitted" || r.StateCode == "Posted") &&
                db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId &&
                    !l.IsDeleted && l.AssetDepreciationRunId == r.Id && l.AssetBookId == book.Id), ct) &&
            !await db.AssetImpairments.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AssetBookId == book.Id && !x.IsDeleted &&
                (x.StateCode == "Submitted" || x.StateCode == "Posted"), ct) &&
            !await db.AssetDisposals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AssetBookId == book.Id && !x.IsDeleted &&
                (x.StateCode == "Submitted" || x.StateCode == "Posted"), ct);
    }

    private async Task<List<JournalLine>?> CapitalizationLinesAsync(
        AssetCapitalizationCommand c, CancellationToken ct)
    {
        var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == c.JournalId && !x.IsDeleted &&
            x.SourceType == "AssetCapitalization" && x.SourceId == c.Id &&
            x.State == JournalState.Posted && x.CurrencyCode == "BDT" &&
            x.CampusId == c.CampusId, ct);
        if (original == null) return null;
        var lines = await db.JournalLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.JournalId == original.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        return lines.Count is >= 2 and <= 3 &&
            lines.Sum(x => x.DebitAmount) > 0m &&
            lines.Sum(x => x.DebitAmount) == lines.Sum(x => x.CreditAmount) &&
            lines.All(x => x.CampusId == c.CampusId && x.AccountId > 0 &&
                x.DebitAmount >= 0m && x.CreditAmount >= 0m &&
                !(x.DebitAmount > 0m && x.CreditAmount > 0m))
                ? lines : null;
    }

    public async Task<ApiResponse<AssetCapitalizationRecordDto>> RequestCapitalizationReversalAsync(
        Guid reference, AssetReversalRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetCapitalizationRecordDto>("Asset Finance correction maker denied.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate == default ||
            !ReasonValid(request.Reason))
            return Conflict<AssetCapitalizationRecordDto>("Correction key, period, date and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var command = await db.AssetCapitalizationCommands.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (command == null || !await CampusAllowedAsync(command.CampusId, ct))
                return Conflict<AssetCapitalizationRecordDto>("Capitalization not found.", 404);
            var prior = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.IdempotencyKey == request.ClientRequestId, ct);
            if (prior != null)
                return !prior.IsDeleted && prior.SourceType == CapReversalSource &&
                    prior.SourceId == command.Id && prior.ReversalOfJournalId == command.JournalId &&
                    prior.AccountingPeriodId == request.AccountingPeriodId &&
                    prior.JournalDate == request.ReversalDate && prior.Description == request.Reason.Trim()
                    ? ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(command, ct),
                        "Existing unit correction returned.")
                    : Conflict<AssetCapitalizationRecordDto>("Correction key belongs to another request.");
            if (!Version(command.RowVersion, request.OriginalRowVersion) ||
                request.ReversalDate < command.CapitalizationDate || request.ReversalDate > Today ||
                !await OpenPeriodAsync(request.AccountingPeriodId, request.ReversalDate, ct) ||
                !await UnwindEligibleAsync(command, ct) ||
                await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.ReversalOfJournalId == command.JournalId && !x.IsDeleted &&
                    x.State != JournalState.Rejected, ct))
                return Conflict<AssetCapitalizationRecordDto>(
                    "Unit has later active valuation/custody, a pending correction or an invalid open period.");
            var lines = await CapitalizationLinesAsync(command, ct);
            if (lines == null) return Conflict<AssetCapitalizationRecordDto>("Original Finance GL does not reconcile.");
            var now = Now;
            var id = Guid.NewGuid();
            var reversal = new Journal
            {
                TenantId = user.TenantId, PublicId = id, IdempotencyKey = request.ClientRequestId,
                JournalNumber = "ACR-" + id.ToString("N").ToUpperInvariant(),
                JournalDate = request.ReversalDate, AccountingPeriodId = request.AccountingPeriodId,
                CampusId = command.CampusId, CurrencyCode = "BDT", SourceType = CapReversalSource,
                SourceId = command.Id, ReversalOfJournalId = command.JournalId,
                State = JournalState.Submitted, Description = request.Reason.Trim(),
                CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(reversal); await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(lines.Select(l => new JournalLine
            {
                TenantId = user.TenantId, JournalId = reversal.Id, LineNo = l.LineNo,
                CampusId = l.CampusId, AccountId = l.AccountId,
                DebitAmount = l.CreditAmount, CreditAmount = l.DebitAmount,
                Description = "Serialized asset capitalization exact GL contra",
                CreatedBy = user.UserId, CreatedAt = now
            }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(command, ct),
                "Independent exact GL and stock unwind requested; no posting yet.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetCapitalizationRecordDto>("Concurrent capitalization correction."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Capitalization reversal SQL conflict"); return Conflict<AssetCapitalizationRecordDto>("Correction SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetCapitalizationRecordDto>("Capitalization correction contention."); }
    }

    public async Task<ApiResponse<AssetCapitalizationRecordDto>> ReviewCapitalizationReversalAsync(
        Guid reference, Guid reversalReference, AssetReviewRequest request, CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetCapitalizationRecordDto>("Independent asset correction reviewer denied.", 403);
        if (reference == Guid.Empty || reversalReference == Guid.Empty || request == null ||
            !ReasonValid(request.Reason))
            return Conflict<AssetCapitalizationRecordDto>("Decision version and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var c = await db.AssetCapitalizationCommands.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (c == null || !await CampusAllowedAsync(c.CampusId, ct))
                return Conflict<AssetCapitalizationRecordDto>("Capitalization not found.", 404);
            var reversal = await db.Journals.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                !x.IsDeleted && x.PublicId == reversalReference && x.SourceType == CapReversalSource &&
                x.SourceId == c.Id && x.ReversalOfJournalId == c.JournalId, ct);
            if (reversal == null) return Conflict<AssetCapitalizationRecordDto>("Correction request not found.", 404);
            if (reversal.State != JournalState.Submitted || !Version(reversal.RowVersion, request.RowVersion) ||
                reversal.CreatedBy is null or <= 0 || reversal.CreatedBy == user.UserId)
                return Conflict<AssetCapitalizationRecordDto>("Stale correction or same maker/reviewer prohibited.");
            if (request.Approve)
            {
                if (reversal.JournalDate < c.CapitalizationDate || reversal.JournalDate > Today ||
                    !await OpenPeriodAsync(reversal.AccountingPeriodId, reversal.JournalDate, ct) ||
                    !await UnwindEligibleAsync(c, ct) ||
                    await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                        !x.IsDeleted && x.Id != reversal.Id && x.ReversalOfJournalId == c.JournalId &&
                        x.State == JournalState.Posted, ct))
                    return Conflict<AssetCapitalizationRecordDto>("Asset changed; posted correction is unsafe.");
                var original = await CapitalizationLinesAsync(c, ct);
                var mirror = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == reversal.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                if (original == null || original.Count != mirror.Count ||
                    original.Where((x, i) => x.LineNo != mirror[i].LineNo ||
                        x.AccountId != mirror[i].AccountId || x.CampusId != mirror[i].CampusId ||
                        x.DebitAmount != mirror[i].CreditAmount ||
                        x.CreditAmount != mirror[i].DebitAmount).Any())
                    return Conflict<AssetCapitalizationRecordDto>("Finance contra differs from original journal.");
                var (stock, error) = await inventory.ReverseAssetIssueInTransactionAsync(c,
                    reversal.IdempotencyKey ?? Guid.Empty, "Reviewed capitalization unwind " +
                    reversal.PublicId.ToString("N") + ": " + request.Reason.Trim(), ct);
                if (stock == null || error != null)
                    return Conflict<AssetCapitalizationRecordDto>(error ?? "Stock correction failed.");
                var book = await db.AssetBooks.FirstAsync(x => x.TenantId == user.TenantId &&
                    x.Id == c.AssetBookId && !x.IsDeleted, ct);
                book.IsActive = false; book.UpdatedAt = Now; book.UpdatedBy = user.UserId;
                // Historical book and original Finance journal remain immutable. A new
                // enrollment requires fresh asset/source evidence, never reuse this key.
                reversal.State = JournalState.Posted;
                reversal.PostedAt = Now; reversal.PostedByUserId = user.UserId;
            }
            else reversal.State = JournalState.Rejected;
            reversal.ApprovedByUserId = user.UserId; reversal.ApprovedAt = Now;
            reversal.UpdatedAt = Now; reversal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AssetCapitalizationRecordDto>.SuccessResponse(await MapAsync(c, ct),
                request.Approve ? "Original unit issue restored and exact Finance GL contra posted atomically."
                    : "Capitalization correction rejected without stock or Finance posting.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetCapitalizationRecordDto>("Concurrent correction review."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset unwind atomic SQL conflict"); return Conflict<AssetCapitalizationRecordDto>("Atomic stock/GL unwind rolled back."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetCapitalizationRecordDto>("Capitalization unwind contention."); }
    }
}
