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
    private sealed record BookValue(AssetBook Book, decimal Depreciation, decimal Impairment,
        decimal Carrying, DateOnly LatestDate);

    private async Task<BookValue?> ValueAsync(long bookId, CancellationToken ct)
    {
        var b = await db.AssetBooks.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.Id == bookId && !x.IsDeleted, ct);
        if (b == null) return null;
        var depreciation = await (from l in db.AssetDepreciationLines.AsNoTracking()
            join r in db.AssetDepreciationRuns.AsNoTracking() on l.AssetDepreciationRunId equals r.Id
            where l.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                l.AssetBookId == b.Id && !l.IsDeleted && !r.IsDeleted && r.StateCode == "Posted" &&
                r.JournalId != null && r.ReversalJournalId == null
            select new { l.Amount, l.PeriodEnd }).ToListAsync(ct);
        var impairments = await db.AssetImpairments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == b.Id && !x.IsDeleted &&
            x.StateCode == "Posted" && x.JournalId != null && x.ReversalJournalId == null)
            .Select(x => new { x.Amount, x.EffectiveDate }).ToListAsync(ct);
        var dep = depreciation.Sum(x => x.Amount);
        var imp = impairments.Sum(x => x.Amount);
        var last = new[] { b.InServiceDate }
            .Concat(depreciation.Select(x => x.PeriodEnd))
            .Concat(impairments.Select(x => x.EffectiveDate)).Max();
        return dep < 0m || imp < 0m || dep + imp > b.CostBasis ? null :
            new BookValue(b, dep, imp, b.CostBasis - dep - imp, last);
    }

    private async Task<(BookValue? Value, string? Error)> EligibleAsync(
        Guid assetReference, long periodId, DateOnly day, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == assetReference && !x.IsDeleted, ct);
        if (asset?.CampusId is not > 0 || !await CampusAllowedAsync(asset.CampusId.Value, ct))
            return (null, "Asset not found or campus inaccessible.");
        var book = await db.AssetBooks.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.AssetId == asset.Id && !x.IsDeleted, ct);
        if (book == null || !book.IsActive || book.CampusId != asset.CampusId)
            return (null, "A live, approved capitalization book is required.");
        if (day == default || day > Today || day < book.InServiceDate ||
            !await OpenPeriodAsync(periodId, day, ct))
            return (null, "Event date must be in an open Finance period and after in-service date.");
        var value = await ValueAsync(book.Id, ct);
        if (value == null || day < value.LatestDate)
            return (null, "Asset valuation is inconsistent or this date predates later posted events.");
        if (await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(r =>
                r.TenantId == user.TenantId && r.StateCode == "Submitted" && !r.IsDeleted &&
                db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId &&
                    l.AssetDepreciationRunId == r.Id && l.AssetBookId == book.Id && !l.IsDeleted), ct) ||
            await db.AssetImpairments.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AssetBookId == book.Id && x.StateCode == "Submitted" && !x.IsDeleted, ct) ||
            await db.AssetDisposals.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.AssetBookId == book.Id && !x.IsDeleted &&
                (x.StateCode == "Submitted" || x.StateCode == "Posted"), ct))
            return (null, "Another pending or posted asset disposal/valuation blocks this operation.");
        return (value, null);
    }

    private static bool ReasonValid(string? reason) => reason?.Trim().Length is >= 10 and <= 800;
    private static bool MonthEnd(DateOnly day) =>
        day.Day == DateTime.DaysInMonth(day.Year, day.Month);

    private async Task<bool> BookAccountsAsync(AssetBook b, CancellationToken ct)
    {
        var ids = new[] { b.CostAccountId, b.AccumulatedDepreciationAccountId,
            b.DepreciationExpenseAccountId, b.ImpairmentExpenseAccountId,
            b.DisposalGainAccountId, b.DisposalLossAccountId };
        if (ids.Any(x => x is null or <= 0) || ids.Distinct().Count() != ids.Length) return false;
        var rows = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            ids.Contains(x.Id) && x.IsActive && !x.IsDeleted && !x.IsGroup &&
            x.AllowsPosting && x.CurrencyCode == "BDT")
            .Select(x => new { x.Id, x.AccountType }).ToListAsync(ct);
        return rows.Count == 6 &&
            rows.Any(x => x.Id == b.CostAccountId && x.AccountType == AccountType.Asset) &&
            rows.Any(x => x.Id == b.AccumulatedDepreciationAccountId && x.AccountType == AccountType.Asset) &&
            rows.Any(x => x.Id == b.DepreciationExpenseAccountId && x.AccountType == AccountType.Expense) &&
            rows.Any(x => x.Id == b.ImpairmentExpenseAccountId && x.AccountType == AccountType.Expense) &&
            rows.Any(x => x.Id == b.DisposalGainAccountId && x.AccountType == AccountType.Income) &&
            rows.Any(x => x.Id == b.DisposalLossAccountId && x.AccountType == AccountType.Expense);
    }

    private async Task<Journal> PostEventAsync(string source, long sourceId, Guid key, long periodId,
        DateOnly day, long campusId, long? makerId, string description,
        (long Account, decimal Debit, decimal Credit)[] lines, CancellationToken ct)
    {
        if (makerId is null or <= 0 || makerId == user.UserId ||
            lines.Length < 2 || lines.Any(x => x.Account <= 0 || x.Debit < 0m ||
                x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m)) ||
            lines.Sum(x => x.Debit) <= 0m || lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit) ||
            lines.Select(x => x.Account).Distinct().Count() != lines.Length ||
            !await OpenPeriodAsync(periodId, day, ct))
            throw new InvalidOperationException("Unbalanced or unauthorized Asset Finance posting.");
        var now = Now;
        var reference = Guid.NewGuid();
        var journal = new Journal
        {
            TenantId = user.TenantId, PublicId = reference, IdempotencyKey = key,
            JournalNumber = "AF-" + reference.ToString("N").ToUpperInvariant(),
            JournalDate = day, AccountingPeriodId = periodId, CampusId = campusId,
            CurrencyCode = "BDT", SourceType = source, SourceId = sourceId,
            Description = description[..Math.Min(description.Length, 1000)],
            State = JournalState.Posted, CreatedAt = now, CreatedBy = makerId,
            ApprovedAt = now, ApprovedByUserId = user.UserId, PostedAt = now,
            PostedByUserId = user.UserId
        };
        db.Journals.Add(journal); await db.SaveChangesAsync(ct);
        db.JournalLines.AddRange(lines.Select((l, index) => new JournalLine
        {
            TenantId = user.TenantId, JournalId = journal.Id, LineNo = index + 1,
            AccountId = l.Account, CampusId = campusId, DebitAmount = l.Debit,
            CreditAmount = l.Credit, Description = source,
            CreatedAt = now, CreatedBy = user.UserId
        }));
        await db.SaveChangesAsync(ct);
        return journal;
    }

    private static AssetAccountingEventDto Event(string kind, long id, Guid requestId, string state,
        DateOnly date, decimal amount, Guid? journal, Guid? reversal, long? maker,
        long? checker, byte[] rowVersion) => new()
        {
            Kind = kind, Id = id, ClientRequestId = requestId, State = state,
            EffectiveDate = date, Amount = amount, JournalReference = journal,
            ReversalJournalReference = reversal, MakerId = maker, ReviewerId = checker,
            RowVersion = Convert.ToBase64String(rowVersion)
        };

    public async Task<ApiResponse<AssetAccountingStatementDto>> StatementAsync(
        Guid assetReference, CancellationToken ct = default)
    {
        if (!Readable) return Conflict<AssetAccountingStatementDto>("Asset Finance access denied.", 403);
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == assetReference && !x.IsDeleted, ct);
        if (asset?.CampusId is not > 0 || !await CampusAllowedAsync(asset.CampusId.Value, ct))
            return Conflict<AssetAccountingStatementDto>("Asset not found.", 404);
        var book = await db.AssetBooks.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.AssetId == asset.Id && !x.IsDeleted, ct);
        var report = new AssetAccountingStatementDto { AssetReference = assetReference };
        if (book == null) return ApiResponse<AssetAccountingStatementDto>.SuccessResponse(report);
        var value = await ValueAsync(book.Id, ct);
        if (value == null) return Conflict<AssetAccountingStatementDto>("Asset ledger reconciliation failed.");
        report.AssetBookId = book.Id; report.CostBasis = book.CostBasis;
        report.SalvageValue = book.SalvageValue; report.PostedDepreciation = value.Depreciation;
        report.PostedImpairment = value.Impairment; report.CarryingValue = value.Carrying;
        report.Disposed = !book.IsActive;
        var runs = await (from l in db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking()
            join r in db.AssetDepreciationRuns.IgnoreQueryFilters().AsNoTracking() on l.AssetDepreciationRunId equals r.Id
            where l.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                l.AssetBookId == book.Id
            select new { Run = r, Line = l }).ToListAsync(ct);
        var impairments = await db.AssetImpairments.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == book.Id).ToListAsync(ct);
        var disposals = await db.AssetDisposals.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssetBookId == book.Id).ToListAsync(ct);
        var journalIds = runs.SelectMany(x => new[] { x.Run.JournalId, x.Run.ReversalJournalId })
            .Concat(impairments.SelectMany(x => new[] { x.JournalId, x.ReversalJournalId }))
            .Concat(disposals.SelectMany(x => new[] { x.JournalId, x.ReversalJournalId }))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var refs = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            journalIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        Guid? Ref(long? id) => id.HasValue && refs.TryGetValue(id.Value, out var value) ? value : null;
        report.Events.AddRange(runs.Select(x => Event("Depreciation", x.Run.Id,
            x.Run.ClientRequestId, x.Run.StateCode, x.Line.PeriodEnd, x.Line.Amount,
            Ref(x.Run.JournalId), Ref(x.Run.ReversalJournalId), x.Run.CreatedBy,
            x.Run.ApprovedByUserId, x.Run.RowVersion)));
        report.Events.AddRange(impairments.Select(x => Event("Impairment", x.Id,
            x.ClientRequestId, x.StateCode, x.EffectiveDate, x.Amount,
            Ref(x.JournalId), Ref(x.ReversalJournalId), x.CreatedBy,
            x.ApprovedByUserId, x.RowVersion)));
        report.Events.AddRange(disposals.Select(x => Event("Disposal", x.Id,
            x.ClientRequestId, x.StateCode, x.DisposalDate, x.Proceeds,
            Ref(x.JournalId), Ref(x.ReversalJournalId), x.CreatedBy,
            x.ApprovedByUserId, x.RowVersion)));
        var reversals = await db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.ReversalOfJournalId.HasValue && journalIds.Contains(x.ReversalOfJournalId.Value) &&
            x.State == JournalState.Submitted && !x.IsDeleted)
            .Select(x => new { x.ReversalOfJournalId, x.SourceType, x.SourceId, x.PublicId, x.RowVersion })
            .ToListAsync(ct);
        foreach (var evt in report.Events)
        {
            var original = evt.JournalReference;
            var journalId = original.HasValue ? refs.FirstOrDefault(x => x.Value == original.Value).Key : 0;
            var pending = reversals.FirstOrDefault(x => x.ReversalOfJournalId == journalId &&
                x.SourceType == "Asset" + evt.Kind + "Reversal" && x.SourceId == evt.Id);
            if (pending == null) continue;
            evt.PendingReversalReference = pending.PublicId;
            evt.PendingReversalRowVersion = Convert.ToBase64String(pending.RowVersion);
        }
        report.Events = report.Events.OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id).ToList();
        return ApiResponse<AssetAccountingStatementDto>.SuccessResponse(report);
    }

    private static decimal AutomaticDepreciation(BookValue value) =>
        decimal.Min(value.Carrying - value.Book.SalvageValue,
            decimal.Round((value.Book.CostBasis - value.Book.SalvageValue) /
                value.Book.UsefulLifeMonths, 2, MidpointRounding.AwayFromZero));

    public async Task<ApiResponse<AssetAccountingEventDto>> RequestDepreciationAsync(
        Guid assetReference, AssetValuationRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Conflict<AssetAccountingEventDto>("Asset Finance maker denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.AccountingPeriodId <= 0 ||
            !MonthEnd(request.EffectiveDate) || !ReasonValid(request.Reason) ||
            request.ValuationMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            !Money(request.Amount))
            return Conflict<AssetAccountingEventDto>("Monthly period end, amount mode, key and reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var replay = await db.AssetDepreciationRuns.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var replayLine = await db.AssetDepreciationLines.IgnoreQueryFilters().AsNoTracking().FirstAsync(x =>
                    x.TenantId == user.TenantId && x.AssetDepreciationRunId == replay.Id, ct);
                return replay.AccountingPeriodId == request.AccountingPeriodId &&
                    replay.PeriodEnd == request.EffectiveDate && replay.ValuationMode == request.ValuationMode &&
                    replay.Remarks == request.Reason.Trim()
                    ? ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Depreciation", replay.Id,
                        replay.ClientRequestId, replay.StateCode, replay.PeriodEnd, replayLine.Amount,
                        null, null, replay.CreatedBy, replay.ApprovedByUserId, replay.RowVersion))
                    : Conflict<AssetAccountingEventDto>("Idempotency key reused with different depreciation.");
            }
            var (value, error) = await EligibleAsync(assetReference, request.AccountingPeriodId,
                request.EffectiveDate, ct);
            if (value == null) return Conflict<AssetAccountingEventDto>(error ?? "Book unavailable.");
            if (!await BookAccountsAsync(value.Book, ct) || value.Book.MethodCode != "StraightLine")
                return Conflict<AssetAccountingEventDto>("Supported active straight-line GL accounts required.");
            var monthNo = (request.EffectiveDate.Year - value.Book.InServiceDate.Year) * 12 +
                request.EffectiveDate.Month - value.Book.InServiceDate.Month;
            if (monthNo < 0 || monthNo >= value.Book.UsefulLifeMonths)
                return Conflict<AssetAccountingEventDto>("Depreciation outside approved useful-life schedule.");
            if (await db.AssetDepreciationLines.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.AssetBookId == value.Book.Id &&
                x.PeriodEnd == request.EffectiveDate && !x.IsDeleted, ct))
                return Conflict<AssetAccountingEventDto>("This book/month already has a depreciation event.");
            var amount = request.ValuationMode == "Automatic" ?
                AutomaticDepreciation(value) : request.Amount;
            if (amount <= 0m || amount > value.Carrying - value.Book.SalvageValue)
                return Conflict<AssetAccountingEventDto>("Depreciation exceeds the undepreciated salvage floor.");
            var run = new AssetDepreciationRun
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                CampusId = value.Book.CampusId, AccountingPeriodId = request.AccountingPeriodId,
                PeriodEnd = request.EffectiveDate, TotalAmount = amount,
                ValuationMode = request.ValuationMode, StateCode = "Submitted",
                Remarks = request.Reason.Trim(), CreatedBy = user.UserId, CreatedAt = Now
            };
            db.AssetDepreciationRuns.Add(run); await db.SaveChangesAsync(ct);
            var line = new AssetDepreciationLine
            {
                TenantId = user.TenantId, AssetDepreciationRunId = run.Id,
                AssetBookId = value.Book.Id, PeriodEnd = run.PeriodEnd, Amount = amount,
                OpeningAccumulated = value.Depreciation,
                ClosingAccumulated = value.Depreciation + amount,
                ClosingCarryingValue = value.Carrying - amount,
                CreatedBy = user.UserId, CreatedAt = Now
            };
            db.AssetDepreciationLines.Add(line); await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Depreciation",
                run.Id, run.ClientRequestId, run.StateCode, run.PeriodEnd, amount,
                null, null, run.CreatedBy, null, run.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Depreciation concurrency conflict."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Asset depreciation source collision"); return Conflict<AssetAccountingEventDto>("Depreciation SQL source collision."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Depreciation SQL contention."); }
    }

    public async Task<ApiResponse<AssetAccountingEventDto>> ReviewDepreciationAsync(
        long id, AssetReviewRequest request, CancellationToken ct = default)
    {
        if (!Reviewer) return Conflict<AssetAccountingEventDto>("Asset Finance reviewer denied.", 403);
        if (request == null || !ReasonValid(request.Reason))
            return Conflict<AssetAccountingEventDto>("Review reason required.", 400);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var run = await db.AssetDepreciationRuns.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (run == null || !await CampusAllowedAsync(run.CampusId, ct))
                return Conflict<AssetAccountingEventDto>("Run not found.", 404);
            if (run.StateCode != "Submitted" || !Version(run.RowVersion, request.RowVersion) ||
                run.CreatedBy is null or <= 0 || run.CreatedBy == user.UserId)
                return Conflict<AssetAccountingEventDto>("Changed run or same maker/reviewer prohibited.");
            var line = await db.AssetDepreciationLines.FirstAsync(x =>
                x.TenantId == user.TenantId && x.AssetDepreciationRunId == run.Id && !x.IsDeleted, ct);
            var now = Now;
            if (!request.Approve)
            {
                run.StateCode = "Rejected"; line.IsDeleted = true;
                line.DeletedAt = now; line.DeletedBy = user.UserId;
            }
            else
            {
                // Pending run itself is ignored here; review under the same Serializable
                // transaction re-checks all other competing states and books.
                var b = await ValueAsync(line.AssetBookId, ct);
                if (b == null || !b.Book.IsActive || b.Book.CampusId != run.CampusId ||
                    !await BookAccountsAsync(b.Book, ct) || run.PeriodEnd < b.LatestDate ||
                    run.PeriodEnd > Today || !await OpenPeriodAsync(run.AccountingPeriodId, run.PeriodEnd, ct) ||
                    line.OpeningAccumulated != b.Depreciation ||
                    line.Amount > b.Carrying - b.Book.SalvageValue ||
                    await db.AssetImpairments.AnyAsync(x => x.TenantId == user.TenantId &&
                        x.AssetBookId == b.Book.Id && x.StateCode == "Submitted" && !x.IsDeleted, ct) ||
                    await db.AssetDisposals.AnyAsync(x => x.TenantId == user.TenantId &&
                        x.AssetBookId == b.Book.Id &&
                        (x.StateCode == "Submitted" || x.StateCode == "Posted") && !x.IsDeleted, ct))
                    return Conflict<AssetAccountingEventDto>("Depreciation inputs changed; re-request valuation.");
                if (await db.AssetDepreciationRuns.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id != run.Id && x.StateCode == "Submitted" && !x.IsDeleted &&
                    db.AssetDepreciationLines.Any(l => l.TenantId == user.TenantId &&
                        l.AssetDepreciationRunId == x.Id && l.AssetBookId == b.Book.Id && !l.IsDeleted), ct))
                    return Conflict<AssetAccountingEventDto>("Competing depreciation command.");
                var journal = await PostEventAsync("AssetDepreciation", run.Id, run.ClientRequestId,
                    run.AccountingPeriodId, run.PeriodEnd, run.CampusId, run.CreatedBy,
                    "Independent monthly depreciation approval: " + request.Reason.Trim(),
                    [(b.Book.DepreciationExpenseAccountId!.Value, line.Amount, 0m),
                     (b.Book.AccumulatedDepreciationAccountId!.Value, 0m, line.Amount)], ct);
                run.StateCode = "Posted"; run.JournalId = journal.Id;
                run.PostedAt = now;
                var expense = await db.JournalLines.AsNoTracking().FirstAsync(x =>
                    x.TenantId == user.TenantId && x.JournalId == journal.Id && x.LineNo == 1, ct);
                line.JournalLineId = expense.Id;
            }
            run.ApprovedByUserId = user.UserId; run.ApprovedAt = now;
            run.UpdatedAt = now; run.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var refId = run.JournalId.HasValue ? await db.Journals.AsNoTracking()
                .Where(x => x.Id == run.JournalId.Value && x.TenantId == user.TenantId)
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
            return ApiResponse<AssetAccountingEventDto>.SuccessResponse(Event("Depreciation",
                run.Id, run.ClientRequestId, run.StateCode, run.PeriodEnd, line.Amount,
                refId, null, run.CreatedBy, run.ApprovedByUserId, run.RowVersion));
        }
        catch (DbUpdateConcurrencyException) { return Conflict<AssetAccountingEventDto>("Concurrent depreciation approval."); }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Depreciation GL conflict"); return Conflict<AssetAccountingEventDto>("Depreciation GL rolled back."); }
        catch (SqlException ex) when (Contention(ex)) { return Conflict<AssetAccountingEventDto>("Depreciation SQL contention."); }
    }
}
