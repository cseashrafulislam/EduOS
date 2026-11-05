using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingSetupService
{
    // A read-only institution-wide close preview. Never mix SaaS subscription billing into this ledger.
    // The write path calls the same checks again while holding its serializable transaction.
    public async Task<ApiResponse<AccountingPeriodCloseReadinessDto>> GetPeriodCloseReadinessAsync(
        long periodId, CancellationToken ct = default)
    {
        if (!Writable) return Denied<AccountingPeriodCloseReadinessDto>();
        if (periodId <= 0)
            return ApiResponse<AccountingPeriodCloseReadinessDto>.ErrorResponse("Valid accounting period required.");
        var period = await Periods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId, ct);
        if (period == null)
            return ApiResponse<AccountingPeriodCloseReadinessDto>.ErrorResponse("Period not found.", 404);

        // Legacy soft deletion must not erase posting history from the close decision.
        // Fail closed and require reconciliation rather than counting hidden journals as zero.
        var historicalDeletedCount = await db.Journals.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.TenantId == user.TenantId && x.AccountingPeriodId == periodId && x.IsDeleted, ct);

        // Deleted journal lines remain financial history, even if the visible
        // portion of a posted journal still balances. Scope both join sides.
        var historicalDeletedLineCount = await (
            from line in db.JournalLines.IgnoreQueryFilters().AsNoTracking()
            join journal in db.Journals.IgnoreQueryFilters().AsNoTracking()
                on new { line.TenantId, Id = line.JournalId }
                equals new { journal.TenantId, journal.Id }
            where line.TenantId == user.TenantId && journal.TenantId == user.TenantId &&
                journal.AccountingPeriodId == periodId && line.IsDeleted
            select line.Id).CountAsync(ct);

        // Validate the entire period in SQL. A fixed row cap made a valid,
        // fully balanced high-volume period impossible to close.
        var journals = db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.AccountingPeriodId == periodId);
        var validStates = Enum.GetValues<JournalState>();
        var summary = await journals.GroupBy(x => 1).Select(g => new
        {
            Draft = g.Count(x => x.State == JournalState.Draft),
            Submitted = g.Count(x => x.State == JournalState.Submitted),
            Approved = g.Count(x => x.State == JournalState.Approved),
            Posted = g.Count(x => x.State == JournalState.Posted),
            Reversed = g.Count(x => x.State == JournalState.Reversed),
            Invalid = g.Count(x => x.JournalDate < period.StartDate || x.JournalDate > period.EndDate ||
                !validStates.Contains(x.State) ||
                ((x.State == JournalState.Posted || x.State == JournalState.Reversed) && x.CurrencyCode != "BDT"))
        }).SingleOrDefaultAsync(ct);

        var result = new AccountingPeriodCloseReadinessDto
        {
            PeriodId = period.Id, PeriodName = period.Name, StartDate = period.StartDate,
            EndDate = period.EndDate, RowVersion = V(period.RowVersion), IsClosed = period.IsClosed,
            DraftCount = summary?.Draft ?? 0,
            SubmittedCount = summary?.Submitted ?? 0,
            ApprovedCount = summary?.Approved ?? 0,
            PostedCount = summary?.Posted ?? 0,
            ReversedCount = summary?.Reversed ?? 0,
            HistoricalDeletedJournalCount = historicalDeletedCount,
            HistoricalDeletedJournalLineCount = historicalDeletedLineCount,
            InvalidJournalCount = summary?.Invalid ?? 0
        };
        var blockers = new List<string>();
        if (period.IsClosed) blockers.Add("This accounting period is already closed.");
        if (historicalDeletedCount > 0)
            blockers.Add("Legacy soft-deleted journals exist in this period; reconcile immutable journal history before closure.");
        if (historicalDeletedLineCount > 0)
            blockers.Add("Legacy soft-deleted journal lines exist in this period; reconcile immutable posting lines before closure.");
        if (result.DraftCount + result.SubmittedCount + result.ApprovedCount > 0)
            blockers.Add("Draft, submitted or approved journals must be resolved before closure.");

        if (result.InvalidJournalCount > 0)
            blockers.Add("Period contains out-of-range dates, invalid states or non-BDT posted journals.");

        if (result.PostedCount + result.ReversedCount > 0)
        {
            var posted = journals.Where(x => x.State == JournalState.Posted || x.State == JournalState.Reversed);
            var lines = from line in db.JournalLines.AsNoTracking()
                join journal in posted on new { line.TenantId, Id = line.JournalId }
                    equals new { journal.TenantId, journal.Id }
                where line.TenantId == user.TenantId && !line.IsDeleted
                select new { line.TenantId, line.JournalId, line.LineNo, line.AccountId,
                    line.CampusId, JournalCampusId = journal.CampusId,
                    line.DebitAmount, line.CreditAmount };

            var lineCount = await lines.CountAsync(ct);
            var distinctNumbers = await lines.Select(x => new { x.JournalId, x.LineNo })
                .Distinct().CountAsync(ct);
            result.InvalidLineCount = await lines.CountAsync(x =>
                x.LineNo <= 0 || x.AccountId <= 0 || x.DebitAmount < 0m || x.CreditAmount < 0m ||
                (x.DebitAmount > 0m && x.CreditAmount > 0m) ||
                (x.DebitAmount == 0m && x.CreditAmount == 0m) ||
                (x.JournalCampusId.HasValue && x.CampusId.HasValue &&
                    x.JournalCampusId != x.CampusId), ct) + lineCount - distinctNumbers;

            result.MissingAccountCount = await (from line in lines
                join account in db.Accounts.AsNoTracking().Where(a =>
                    a.TenantId == user.TenantId && !a.IsDeleted)
                    on new { line.TenantId, Id = line.AccountId }
                    equals new { account.TenantId, account.Id } into matching
                from account in matching.DefaultIfEmpty()
                where account == null || account.CurrencyCode != "BDT" ||
                    account.IsGroup || !account.AllowsPosting
                select line.JournalId).CountAsync(ct);
            if (result.MissingAccountCount > 0)
                blockers.Add("Posted lines reference missing or non-postable BDT accounts.");

            var withLines = await lines.Select(x => x.JournalId).Distinct().CountAsync(ct);
            var unbalancedWithLines = await lines.GroupBy(x => x.JournalId)
                .Where(g => g.Count() < 2 || g.Sum(x => x.DebitAmount) <= 0m ||
                    g.Sum(x => x.DebitAmount) != g.Sum(x => x.CreditAmount))
                .CountAsync(ct);
            result.UnbalancedJournalCount = result.PostedCount + result.ReversedCount - withLines + unbalancedWithLines;
            if (result.InvalidLineCount > 0)
                blockers.Add("Posted journal lines have invalid debit/credit, duplicate line numbers or campus attribution.");
            if (result.UnbalancedJournalCount > 0)
                blockers.Add("Posted journals must have at least two balanced, nonzero lines.");
        }

        result.Blockers = blockers;
        result.CanClose = blockers.Count == 0;
        return ApiResponse<AccountingPeriodCloseReadinessDto>.SuccessResponse(result);
    }
}
