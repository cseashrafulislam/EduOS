using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Enums.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetAccountingService
{
    public async Task<ApiResponse<AssetAccountingEventDto>> RequestImpairmentAsync(
        Guid assetReference, AssetValuationRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetAccountingEventDto>("Asset Finance maker denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || !ReasonValid(request.Reason) ||
            request.ValuationMode != "ManualFinal" || !Money(request.Amount) || request.Amount <= 0m)
            return Conflict<AssetAccountingEventDto>("Documented manual impairment, date, period and amount required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var prior = await db.AssetImpairments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
                return prior.AccountingPeriodId == request.AccountingPeriodId &&
                    prior.EffectiveDate == request.EffectiveDate && prior.Amount == request.Amount &&
                    prior.Reason == request.Reason.Trim()
                    ? ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Impairment", prior.Id,
                        prior.ClientRequestId, prior.StateCode, prior.EffectiveDate, prior.Amount,
                        null, null, prior.CreatedBy, prior.ApprovedByUserId, prior.RowVersion))
                    : Conflict<AssetAccountingEventDto>("Impairment key is already used.");
            var (value, error) = await EligibleAsync(assetReference, request.AccountingPeriodId,
                request.EffectiveDate, ct);
            if (value == null) return Conflict<AssetAccountingEventDto>(error ?? "Asset not eligible.");
            if (!await BookAccountsAsync(value.Book, ct) ||
                request.Amount > value.Carrying - await EffectiveSalvageAsync(value.Book, request.EffectiveDate, ct))
                return Conflict<AssetAccountingEventDto>("Impairment exceeds current salvage-protected carrying value.");
            var imp = new AssetImpairment
            {
                TenantId = user.TenantId, AssetBookId = value.Book.Id,
                ClientRequestId = request.ClientRequestId, AccountingPeriodId = request.AccountingPeriodId,
                EffectiveDate = request.EffectiveDate, Amount = request.Amount,
                StateCode = "Submitted", Reason = request.Reason.Trim(),
                CreatedAt = Now, CreatedBy = user.UserId
            };
            db.AssetImpairments.Add(imp); await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Impairment", imp.Id,
                imp.ClientRequestId, imp.StateCode, imp.EffectiveDate, imp.Amount,
                null, null, imp.CreatedBy, null, imp.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent impairment."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Impairment source SQL conflict"); return Conflict<AssetAccountingEventDto>("Impairment SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Impairment SQL contention."); }
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> ReviewImpairmentAsync(
        long id, AssetReviewRequest request, CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetAccountingEventDto>("Asset Finance reviewer denied.", 403);
        if (request == null || !ReasonValid(request.Reason))
            return Conflict<AssetAccountingEventDto>("Review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var imp = await db.AssetImpairments.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            var b = imp == null ? null : await ValueAsync(imp.AssetBookId, ct);
            if (imp == null || b == null || !await CampusAllowedAsync(b.Book.CampusId, ct))
                return Conflict<AssetAccountingEventDto>("Impairment not found.", 404);
            if (imp.StateCode != "Submitted" || !Version(imp.RowVersion, request.RowVersion) ||
                imp.CreatedBy is null or <= 0 || imp.CreatedBy == user.UserId)
                return Conflict<AssetAccountingEventDto>("Changed impairment or same maker/reviewer prohibited.");
            if (!request.Approve) imp.StateCode = "Rejected";
            else
            {
                if (!b.Book.IsActive || imp.AccountingPeriodId is null or <= 0 ||
                    imp.EffectiveDate < b.LatestDate || imp.EffectiveDate > Today ||
                    imp.Amount > b.Carrying - await EffectiveSalvageAsync(b.Book, imp.EffectiveDate, ct) ||
                    !await BookAccountsAsync(b.Book, ct) ||
                    !await OpenPeriodAsync(imp.AccountingPeriodId.Value, imp.EffectiveDate, ct) ||
                    await CompetingAsync(b.Book.Id, "Impairment", imp.Id, ct))
                    return Conflict<AssetAccountingEventDto>("Impairment valuation/period changed; request fresh approval.");
                // Directly reduce fixed-asset GL carrying value; the immutable book
                // retains original cost and the impairment row explains the difference.
                var journal = await PostEventAsync("AssetImpairment", imp.Id, imp.ClientRequestId,
                    imp.AccountingPeriodId.Value, imp.EffectiveDate, b.Book.CampusId,
                    imp.CreatedBy, "Approved asset impairment: " + request.Reason.Trim(),
                    [(b.Book.ImpairmentExpenseAccountId!.Value, imp.Amount, 0m),
                     (b.Book.CostAccountId!.Value, 0m, imp.Amount)], ct);
                imp.StateCode = "Posted"; imp.JournalId = journal.Id;
            }
            imp.ApprovedByUserId = user.UserId; imp.ApprovedAt = Now;
            imp.UpdatedAt = Now; imp.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var j = imp.JournalId.HasValue ? await db.Journals.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.Id == imp.JournalId.Value)
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Impairment",
                imp.Id, imp.ClientRequestId, imp.StateCode, imp.EffectiveDate, imp.Amount,
                j, null, imp.CreatedBy, imp.ApprovedByUserId, imp.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent impairment approval."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Impairment GL conflict"); return Conflict<AssetAccountingEventDto>("Impairment GL rolled back."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Impairment SQL contention."); }
    }

    private async Task<bool> CompetingAsync(long bookId, string ownKind, long ownId, CancellationToken ct)
    {
        return await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.StateCode == "Submitted" && !x.IsDeleted &&
                (ownKind != "Depreciation" || x.Id != ownId) &&
                db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId &&
                    l.AssetDepreciationRunId == x.Id && l.AssetBookId == bookId && !l.IsDeleted), ct) ||
            await db.AssetImpairments.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetBookId == bookId &&
                x.StateCode == "Submitted" && !x.IsDeleted &&
                (ownKind != "Impairment" || x.Id != ownId), ct) ||
            await db.AssetDisposals.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetBookId == bookId && !x.IsDeleted &&
                ((x.StateCode == "Posted" && (ownKind != "Disposal" || x.Id != ownId)) ||
                 (x.StateCode == "Submitted" &&
                    (ownKind != "Disposal" || x.Id != ownId))), ct);
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> RequestDisposalAsync(
        Guid assetReference, AssetValuationRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetAccountingEventDto>("Asset Finance maker denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || !ReasonValid(request.Reason) ||
            request.ValuationMode != "ManualFinal" || !Money(request.Amount))
            return Conflict<AssetAccountingEventDto>("Documented proceeds, date, period and clearance required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var prior = await db.AssetDisposals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
                return prior.AccountingPeriodId == request.AccountingPeriodId &&
                    prior.DisposalDate == request.EffectiveDate && prior.Proceeds == request.Amount &&
                    prior.ProceedsClearingAccountId == request.ProceedsClearingAccountId &&
                    prior.Reason == request.Reason.Trim()
                    ? ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Disposal", prior.Id,
                        prior.ClientRequestId, prior.StateCode, prior.DisposalDate, prior.Proceeds,
                        null, null, prior.CreatedBy, prior.ApprovedByUserId, prior.RowVersion))
                    : Conflict<AssetAccountingEventDto>("Disposal key already belongs to another request.");
            var (value, error) = await EligibleAsync(assetReference, request.AccountingPeriodId,
                request.EffectiveDate, ct);
            if (value == null) return Conflict<AssetAccountingEventDto>(error ?? "Asset not eligible.");
            if (!await BookAccountsAsync(value.Book, ct) ||
                !await ProceedsAccountAsync(value.Book, request.ProceedsClearingAccountId, request.Amount, ct))
                return Conflict<AssetAccountingEventDto>("Active proceeds clearing and Finance book accounts required.");
            var asset = await db.Assets.AsNoTracking().FirstAsync(x => x.TenantId == user.TenantId &&
                x.Id == value.Book.AssetId, ct);
            if (asset.StateCode != "Available" || await db.AssetAssignments.AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetId == asset.Id && !x.IsDeleted &&
                x.ReturnedOn == null, ct))
                return Conflict<AssetAccountingEventDto>("Return asset custody before disposal.");
            var disposal = new AssetDisposal
            {
                TenantId = user.TenantId, AssetBookId = value.Book.Id,
                ClientRequestId = request.ClientRequestId,
                AccountingPeriodId = request.AccountingPeriodId,
                ProceedsClearingAccountId = request.ProceedsClearingAccountId,
                DisposalDate = request.EffectiveDate, Proceeds = request.Amount,
                AccumulatedDepreciation = value.Depreciation, CarryingValue = value.Carrying,
                GainLoss = request.Amount - value.Carrying,
                StateCode = "Submitted", Reason = request.Reason.Trim(),
                CreatedAt = Now, CreatedBy = user.UserId
            };
            db.AssetDisposals.Add(disposal); await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Disposal",
                disposal.Id, disposal.ClientRequestId, disposal.StateCode,
                disposal.DisposalDate, disposal.Proceeds, null, null, disposal.CreatedBy,
                null, disposal.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent disposal."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Disposal source SQL conflict"); return Conflict<AssetAccountingEventDto>("Disposal SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Disposal SQL contention."); }
    }

    private async Task<bool> ProceedsAccountAsync(AssetBook b, long? accountId, decimal amount, CancellationToken ct)
    {
        if (amount == 0m) return accountId is null or 0;
        if (accountId is null or <= 0 ||
            accountId == b.CostAccountId || accountId == b.AccumulatedDepreciationAccountId ||
            accountId == b.DisposalLossAccountId || accountId == b.DisposalGainAccountId)
            return false;
        return await db.Accounts.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.Id == accountId && x.AccountType == AccountType.Asset && !x.IsDeleted &&
            x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT", ct);
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> ReviewDisposalAsync(
        long id, AssetReviewRequest request, CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetAccountingEventDto>("Asset Finance reviewer denied.", 403);
        if (request == null || !ReasonValid(request.Reason))
            return Conflict<AssetAccountingEventDto>("Review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var disposal = await db.AssetDisposals.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            var value = disposal == null ? null : await ValueAsync(disposal.AssetBookId, ct);
            if (disposal == null || value == null ||
                !await CampusAllowedAsync(value.Book.CampusId, ct))
                return Conflict<AssetAccountingEventDto>("Disposal not found.", 404);
            if (disposal.StateCode != "Submitted" || !Version(disposal.RowVersion, request.RowVersion) ||
                disposal.CreatedBy is null or <= 0 || disposal.CreatedBy == user.UserId)
                return Conflict<AssetAccountingEventDto>("Changed disposal or same maker/reviewer prohibited.");
            if (!request.Approve)
            {
                // Existing unique tenant/book index has no rejection filter; preserve
                // the rejected audit row as a tombstone so a new request can be made.
                disposal.StateCode = "Rejected"; disposal.IsDeleted = true;
                disposal.DeletedAt = Now; disposal.DeletedBy = user.UserId;
            }
            else
            {
                var asset = await db.Assets.FirstAsync(x => x.TenantId == user.TenantId &&
                    x.Id == value.Book.AssetId, ct);
                if (!value.Book.IsActive || disposal.AccountingPeriodId is null or <= 0 ||
                    disposal.DisposalDate < value.LatestDate || disposal.DisposalDate > Today ||
                    disposal.CarryingValue != value.Carrying ||
                    disposal.AccumulatedDepreciation != value.Depreciation ||
                    !await BookAccountsAsync(value.Book, ct) ||
                    !await OpenPeriodAsync(disposal.AccountingPeriodId.Value, disposal.DisposalDate, ct) ||
                    !await ProceedsAccountAsync(value.Book, disposal.ProceedsClearingAccountId,
                        disposal.Proceeds, ct) ||
                    asset.StateCode != "Available" ||
                    await db.AssetAssignments.AnyAsync(x => x.TenantId == user.TenantId &&
                        x.AssetId == asset.Id && !x.IsDeleted && x.ReturnedOn == null, ct) ||
                    await CompetingAsync(value.Book.Id, "Disposal", disposal.Id, ct))
                    return Conflict<AssetAccountingEventDto>("Disposal custody or financial balance changed; reload.");
                var entries = new List<(long Account, decimal Debit, decimal Credit)>();
                if (disposal.Proceeds > 0m)
                    entries.Add((disposal.ProceedsClearingAccountId!.Value, disposal.Proceeds, 0m));
                if (value.Depreciation > 0m)
                    entries.Add((value.Book.AccumulatedDepreciationAccountId!.Value, value.Depreciation, 0m));
                var netCost = value.Book.CostBasis - value.Impairment;
                entries.Add((value.Book.CostAccountId!.Value, 0m, netCost));
                if (disposal.GainLoss > 0m)
                    entries.Add((value.Book.DisposalGainAccountId!.Value, 0m, disposal.GainLoss));
                if (disposal.GainLoss < 0m)
                    entries.Add((value.Book.DisposalLossAccountId!.Value, -disposal.GainLoss, 0m));
                var journal = await PostEventAsync("AssetDisposal", disposal.Id, disposal.ClientRequestId,
                    disposal.AccountingPeriodId.Value, disposal.DisposalDate, value.Book.CampusId,
                    disposal.CreatedBy, "Approved asset disposal; proceeds are receivable/clearing, not bank receipt. " +
                        request.Reason.Trim(), entries.ToArray(), ct);
                disposal.StateCode = "Posted"; disposal.JournalId = journal.Id;
                value.Book.IsActive = false; value.Book.UpdatedAt = Now; value.Book.UpdatedBy = user.UserId;
                asset.StateCode = "Disposed"; asset.IsActive = false;
                asset.UpdatedAt = Now; asset.UpdatedBy = user.UserId;
            }
            disposal.ApprovedByUserId = user.UserId; disposal.ApprovedAt = Now;
            disposal.UpdatedAt = Now; disposal.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var refId = disposal.JournalId.HasValue ? await db.Journals.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.Id == disposal.JournalId.Value)
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Disposal",
                disposal.Id, disposal.ClientRequestId, disposal.StateCode, disposal.DisposalDate,
                disposal.Proceeds, refId, null, disposal.CreatedBy, disposal.ApprovedByUserId,
                disposal.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent disposal approval."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Disposal GL conflict"); return Conflict<AssetAccountingEventDto>("Disposal GL rolled back."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Disposal SQL contention."); }
    }
}
