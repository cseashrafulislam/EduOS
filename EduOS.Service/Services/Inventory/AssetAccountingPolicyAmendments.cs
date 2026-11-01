using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetAccountingService
{
    private static int MonthsBetween(DateOnly start, DateOnly end) =>
        (end.Year - start.Year) * 12 + end.Month - start.Month;

    private Task<AssetDepreciationPolicyAmendment?> EffectivePolicyAsync(
        long bookId, DateOnly day, CancellationToken ct) =>
        db.AssetDepreciationPolicyAmendments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == bookId &&
            x.StateCode == "Approved" && !x.IsDeleted && x.EffectiveFrom <= day)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<decimal> EffectiveSalvageAsync(AssetBook b, DateOnly day, CancellationToken ct)
    {
        var policy = await EffectivePolicyAsync(b.Id, day, ct);
        if (policy != null) return policy.NewSalvageValue;
        var earliest = await db.AssetDepreciationPolicyAmendments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == b.Id &&
            x.StateCode == "Approved" && !x.IsDeleted)
            .OrderBy(x => x.EffectiveFrom).Select(x => (decimal?)x.PreviousSalvageValue)
            .FirstOrDefaultAsync(ct);
        return earliest ?? b.SalvageValue;
    }

    private async Task<(decimal Amount, bool InSchedule)> AutomaticDepreciationForDateAsync(
        BookValue value, DateOnly day, CancellationToken ct)
    {
        var policy = await EffectivePolicyAsync(value.Book.Id, day, ct);
        var months = policy == null ? MonthsBetween(value.Book.InServiceDate, day) :
            MonthsBetween(policy.EffectiveFrom, day);
        var life = policy?.ApprovedRemainingMonths ?? value.Book.UsefulLifeMonths;
        var salvage = policy?.NewSalvageValue ?? await EffectiveSalvageAsync(value.Book, day, ct);
        if (months < 0 || months >= life) return (0m, false);
        var basis = policy?.OpeningCarryingValue ?? value.Book.CostBasis;
        return (Math.Min(value.Carrying - salvage, decimal.Round((basis - salvage) / life,
            2, MidpointRounding.AwayFromZero)), true);
    }

    private async Task<int> SuggestedRemainingAsync(AssetBook b, DateOnly day, CancellationToken ct)
    {
        var previous = await db.AssetDepreciationPolicyAmendments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == b.Id &&
            x.StateCode == "Approved" && !x.IsDeleted && x.EffectiveFrom <= day)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Select(x => new { x.EffectiveFrom, x.ApprovedRemainingMonths }).FirstOrDefaultAsync(ct);
        return previous == null ? Math.Max(1, b.UsefulLifeMonths - MonthsBetween(b.InServiceDate, day)) :
            Math.Max(1, previous.ApprovedRemainingMonths - MonthsBetween(previous.EffectiveFrom, day));
    }

    private async Task<bool> HasLaterPolicyEventsAsync(long bookId, DateOnly start, CancellationToken ct) =>
        await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && (x.StateCode == "Submitted" || x.StateCode == "Posted") &&
            db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId && !l.IsDeleted &&
                l.AssetDepreciationRunId == x.Id && l.AssetBookId == bookId && l.PeriodEnd >= start), ct) ||
        await db.AssetImpairments.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.AssetBookId == bookId &&
            (x.StateCode == "Submitted" || x.StateCode == "Posted") && x.EffectiveDate >= start, ct) ||
        await db.AssetDisposals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.AssetBookId == bookId &&
            (x.StateCode == "Submitted" || x.StateCode == "Posted"), ct);

    private async Task<AssetDepreciationPolicyDto> PolicyDtoAsync(
        AssetDepreciationPolicyAmendment p, CancellationToken ct)
    {
        var a = await (from b in db.AssetBooks.AsNoTracking()
            join asset in db.Assets.AsNoTracking() on b.AssetId equals asset.Id
            where b.TenantId == user.TenantId && asset.TenantId == user.TenantId && b.Id == p.AssetBookId
            select asset.PublicId).FirstAsync(ct);
        return new AssetDepreciationPolicyDto
        {
            Reference = p.PublicId, AssetReference = a, State = p.StateCode,
            Mode = p.Mode, EffectiveFrom = p.EffectiveFrom,
            PreviousPolicyVersion = p.PreviousPolicyVersion,
            NewPolicyVersion = p.NewPolicyVersion,
            SuggestedRemainingMonths = p.SuggestedRemainingMonths,
            ApprovedRemainingMonths = p.ApprovedRemainingMonths,
            OpeningCarryingValue = p.OpeningCarryingValue,
            NewSalvageValue = p.NewSalvageValue,
            MakerId = p.CreatedBy, ReviewerId = p.ApprovedByUserId,
            RowVersion = Convert.ToBase64String(p.RowVersion)
        };
    }

    public async Task<ApiResponse<AssetDepreciationPolicyDto>> RequestDepreciationPolicyAsync(
        Guid assetReference, CreateAssetDepreciationPolicyRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetDepreciationPolicyDto>("Asset policy maker denied.", 403);
        if (assetReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.NewPolicyVersion <= 1 || request.AccountingPeriodId <= 0 ||
            request.EffectiveFrom == default || request.EffectiveFrom.Day != 1 ||
            request.Mode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            request.RemainingUsefulLifeMonths is < 0 or > 1200 ||
            request.RemainingLifeCorrectionMonths is < -1200 or > 1200 ||
            !Money(request.NewSalvageValue) || !ReasonValid(request.Reason))
            return Conflict<AssetDepreciationPolicyDto>("First-of-month date, dynamic policy, period and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var a = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == assetReference && !x.IsDeleted, ct);
            if (a?.CampusId is not > 0 || !await CampusAllowedAsync(a.CampusId.Value, ct))
                return Conflict<AssetDepreciationPolicyDto>("Asset not found.", 404);
            var book = await db.AssetBooks.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.AssetId == a.Id && x.IsActive && !x.IsDeleted, ct);
            if (book == null) return Conflict<AssetDepreciationPolicyDto>("Active asset book required.");
            var replay = await db.AssetDepreciationPolicyAmendments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var same = !replay.IsDeleted && replay.AssetBookId == book.Id &&
                    replay.EffectiveFrom == request.EffectiveFrom && replay.Mode == request.Mode &&
                    replay.NewPolicyVersion == request.NewPolicyVersion &&
                    replay.NewSalvageValue == request.NewSalvageValue &&
                    replay.AccountingPeriodId == request.AccountingPeriodId &&
                    replay.Reason == request.Reason.Trim() &&
                    (request.Mode == "Automatic" ? request.RemainingUsefulLifeMonths == 0 &&
                        request.RemainingLifeCorrectionMonths == 0 :
                        request.Mode == "ManualFinal" ?
                            request.RemainingUsefulLifeMonths == replay.ApprovedRemainingMonths &&
                            request.RemainingLifeCorrectionMonths == 0 :
                            request.RemainingUsefulLifeMonths == 0 &&
                            request.RemainingLifeCorrectionMonths == replay.RemainingLifeCorrectionMonths);
                return same ? ApiResponse<AssetDepreciationPolicyDto>.SuccessResponse(
                    await PolicyDtoAsync(replay, ct), "Existing policy command returned.") :
                    Conflict<AssetDepreciationPolicyDto>("Policy key belongs to different inputs.");
            }
            var value = await ValueAsync(book.Id, ct);
            if (value == null || !Version(book.RowVersion, request.AssetBookRowVersion) ||
                request.EffectiveFrom < book.InServiceDate ||
                request.EffectiveFrom < new DateOnly(Today.Year, Today.Month, 1) ||
                request.NewPolicyVersion != book.PolicyVersion + 1 ||
                request.NewSalvageValue > value.Carrying ||
                !await OpenPeriodAsync(request.AccountingPeriodId, request.EffectiveFrom, ct) ||
                await HasLaterPolicyEventsAsync(book.Id, request.EffectiveFrom, ct) ||
                await db.AssetDepreciationPolicyAmendments.AnyAsync(x =>
                    x.TenantId == user.TenantId && x.AssetBookId == book.Id && !x.IsDeleted &&
                    x.StateCode == "Approved" && x.EffectiveFrom > request.EffectiveFrom, ct) ||
                await db.Journals.AnyAsync(j => j.TenantId == user.TenantId && !j.IsDeleted &&
                    j.SourceType == "AssetCapitalizationReversal" &&
                    db.AssetCapitalizationCommands.Any(c => c.TenantId == user.TenantId &&
                        c.AssetBookId == book.Id && c.Id == j.SourceId && !c.IsDeleted) &&
                    j.State == EduOS.Core.Enums.Domain.JournalState.Submitted, ct) ||
                await db.AssetDepreciationPolicyAmendments.AnyAsync(x =>
                    x.TenantId == user.TenantId && x.AssetBookId == book.Id && !x.IsDeleted &&
                    x.StateCode == "Submitted", ct))
                return Conflict<AssetDepreciationPolicyDto>("Policy source, future schedule or period changed.");
            var suggested = await SuggestedRemainingAsync(book, request.EffectiveFrom, ct);
            var approved = request.Mode switch
            {
                "Automatic" => suggested,
                "ManualFinal" => request.RemainingUsefulLifeMonths,
                _ => suggested + request.RemainingLifeCorrectionMonths
            };
            if (approved is < 1 or > 1200 ||
                (request.Mode == "Automatic" && (request.RemainingUsefulLifeMonths != 0 ||
                    request.RemainingLifeCorrectionMonths != 0 ||
                    request.NewSalvageValue != await EffectiveSalvageAsync(book, request.EffectiveFrom, ct))) ||
                (request.Mode == "ManualFinal" && request.RemainingLifeCorrectionMonths != 0) ||
                (request.Mode == "AutoPlusManual" && request.RemainingUsefulLifeMonths != 0))
                return Conflict<AssetDepreciationPolicyDto>("Manual final or signed correction must be exclusive.", 409);
            var row = new AssetDepreciationPolicyAmendment
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                AssetBookId = book.Id, CampusId = book.CampusId,
                AccountingPeriodId = request.AccountingPeriodId, EffectiveFrom = request.EffectiveFrom,
                PreviousPolicyVersion = book.PolicyVersion, NewPolicyVersion = request.NewPolicyVersion,
                SuggestedRemainingMonths = suggested,
                RemainingLifeCorrectionMonths = request.RemainingLifeCorrectionMonths,
                ApprovedRemainingMonths = approved, OpeningCarryingValue = value.Carrying,
                PreviousSalvageValue = await EffectiveSalvageAsync(book, request.EffectiveFrom, ct),
                NewSalvageValue = request.NewSalvageValue, Mode = request.Mode,
                StateCode = "Submitted", Reason = request.Reason.Trim(),
                CreatedAt = Now, CreatedBy = user.UserId
            };
            db.AssetDepreciationPolicyAmendments.Add(row);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AssetDepreciationPolicyDto>.SuccessResponse(await PolicyDtoAsync(row, ct),
                "Prospective depreciation policy submitted; no posted journal changed.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetDepreciationPolicyDto>("Concurrent policy submission."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset policy SQL conflict"); return Conflict<AssetDepreciationPolicyDto>("Policy SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetDepreciationPolicyDto>("Policy SQL contention."); }
    }

    public async Task<ApiResponse<AssetDepreciationPolicyDto>> ReviewDepreciationPolicyAsync(
        Guid reference, AssetReviewRequest request, CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetDepreciationPolicyDto>("Independent policy reviewer denied.", 403);
        if (reference == Guid.Empty || request == null || !ReasonValid(request.Reason))
            return Conflict<AssetDepreciationPolicyDto>("Policy decision reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var p = await db.AssetDepreciationPolicyAmendments.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (p == null || !await CampusAllowedAsync(p.CampusId, ct))
                return Conflict<AssetDepreciationPolicyDto>("Policy not found.", 404);
            if (p.StateCode != "Submitted" || !Version(p.RowVersion, request.RowVersion) ||
                p.CreatedBy is null or <= 0 || p.CreatedBy == user.UserId)
                return Conflict<AssetDepreciationPolicyDto>("Policy changed or same maker/checker prohibited.");
            if (request.Approve)
            {
                var book = await db.AssetBooks.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.Id == p.AssetBookId && !x.IsDeleted, ct);
                var value = book == null ? null : await ValueAsync(book.Id, ct);
                if (book == null || !book.IsActive || value == null ||
                    p.EffectiveFrom < new DateOnly(Today.Year, Today.Month, 1) ||
                    book.PolicyVersion != p.PreviousPolicyVersion ||
                    p.NewPolicyVersion != p.PreviousPolicyVersion + 1 ||
                    p.NewSalvageValue > value.Carrying ||
                    p.OpeningCarryingValue != value.Carrying ||
                    !await OpenPeriodAsync(p.AccountingPeriodId, p.EffectiveFrom, ct) ||
                    await db.AssetDepreciationPolicyAmendments.AnyAsync(x =>
                        x.TenantId == user.TenantId && x.AssetBookId == book.Id && !x.IsDeleted &&
                        x.StateCode == "Approved" && x.EffectiveFrom > p.EffectiveFrom, ct) ||
                    await HasLaterPolicyEventsAsync(book.Id, p.EffectiveFrom, ct))
                    return Conflict<AssetDepreciationPolicyDto>("Book/value/period changed; policy must be re-requested.");
                // Never edit original cost, posted depreciation lines or journals.
                // Version controls the newest approved prospective schedule.
                book.PolicyVersion = p.NewPolicyVersion;
                book.UpdatedAt = Now; book.UpdatedBy = user.UserId;
            }
            p.StateCode = request.Approve ? "Approved" : "Rejected";
            p.ApprovedByUserId = user.UserId; p.ApprovedAtUtc = Now;
            p.ReviewReason = request.Reason.Trim();
            p.UpdatedAt = Now; p.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return ApiResponse<AssetDepreciationPolicyDto>.SuccessResponse(await PolicyDtoAsync(p, ct),
                request.Approve ? "Prospective schedule approved; historical GL untouched."
                    : "Policy amendment rejected without modifying the book.");
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetDepreciationPolicyDto>("Concurrent policy review."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset policy review conflict"); return Conflict<AssetDepreciationPolicyDto>("Policy review SQL conflict."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetDepreciationPolicyDto>("Policy SQL contention."); }
    }
}
