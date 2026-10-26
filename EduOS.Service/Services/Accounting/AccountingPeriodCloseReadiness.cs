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

        var journals = await db.Journals.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && x.AccountingPeriodId == periodId)
            .Select(x => new { x.Id, x.JournalDate, x.State, x.CurrencyCode, x.CampusId })
            .OrderBy(x => x.Id).Take(10001).ToListAsync(ct);
        if (journals.Count > 10000)
            return ApiResponse<AccountingPeriodCloseReadinessDto>.ErrorResponse(
                "More than 10,000 period journals; use a controlled reconciliation export.", 422);

        var result = new AccountingPeriodCloseReadinessDto
        {
            PeriodId = period.Id, PeriodName = period.Name, StartDate = period.StartDate,
            EndDate = period.EndDate, RowVersion = V(period.RowVersion), IsClosed = period.IsClosed,
            DraftCount = journals.Count(x => x.State == JournalState.Draft),
            SubmittedCount = journals.Count(x => x.State == JournalState.Submitted),
            ApprovedCount = journals.Count(x => x.State == JournalState.Approved),
            PostedCount = journals.Count(x => x.State == JournalState.Posted),
            ReversedCount = journals.Count(x => x.State == JournalState.Reversed)
        };
        var blockers = new List<string>();
        if (period.IsClosed) blockers.Add("This accounting period is already closed.");
        if (result.DraftCount + result.SubmittedCount + result.ApprovedCount > 0)
            blockers.Add("Draft, submitted or approved journals must be resolved before closure.");

        var posted = journals.Where(x => x.State == JournalState.Posted || x.State == JournalState.Reversed).ToArray();
        result.InvalidJournalCount = journals.Count(x =>
            x.JournalDate < period.StartDate || x.JournalDate > period.EndDate ||
            !Enum.IsDefined(x.State) ||
            ((x.State == JournalState.Posted || x.State == JournalState.Reversed) && x.CurrencyCode != "BDT"));
        if (result.InvalidJournalCount > 0)
            blockers.Add("Period contains out-of-range dates, invalid states or non-BDT posted journals.");

        if (posted.Length > 0)
        {
            var lines = await (from line in db.JournalLines.AsNoTracking()
                join journal in db.Journals.AsNoTracking() on line.JournalId equals journal.Id
                where line.TenantId == user.TenantId && journal.TenantId == user.TenantId &&
                    !line.IsDeleted && !journal.IsDeleted && journal.AccountingPeriodId == periodId &&
                    (journal.State == JournalState.Posted || journal.State == JournalState.Reversed)
                select new { line.JournalId, line.LineNo, line.AccountId, line.CampusId,
                    line.DebitAmount, line.CreditAmount })
                .Take(30001).ToListAsync(ct);
            if (lines.Count > 30000)
                return ApiResponse<AccountingPeriodCloseReadinessDto>.ErrorResponse(
                    "More than 30,000 posted journal lines; use a controlled reconciliation export.", 422);

            var accountIds = lines.Select(x => x.AccountId).Distinct().ToArray();
            var accounts = await db.Accounts.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CurrencyCode, x.IsGroup, x.AllowsPosting })
                .ToListAsync(ct);
            var validAccountIds = accounts.Where(x => x.CurrencyCode == "BDT" &&
                !x.IsGroup && x.AllowsPosting).Select(x => x.Id).ToHashSet();
            result.MissingAccountCount = lines.Count(x => !validAccountIds.Contains(x.AccountId));
            if (result.MissingAccountCount > 0)
                blockers.Add("Posted lines reference missing or non-postable BDT accounts.");

            var byJournal = lines.ToLookup(x => x.JournalId);
            foreach (var journal in posted)
            {
                var entries = byJournal[journal.Id].ToArray();
                result.InvalidLineCount += entries.Count(x =>
                    x.LineNo <= 0 || x.AccountId <= 0 || x.DebitAmount < 0m || x.CreditAmount < 0m ||
                    (x.DebitAmount > 0m && x.CreditAmount > 0m) ||
                    (x.DebitAmount == 0m && x.CreditAmount == 0m) ||
                    (journal.CampusId.HasValue && x.CampusId.HasValue && journal.CampusId != x.CampusId));
                result.InvalidLineCount += entries.Length - entries.Select(x => x.LineNo).Distinct().Count();
                if (entries.Length < 2 ||
                    entries.Sum(x => x.DebitAmount) <= 0m ||
                    entries.Sum(x => x.DebitAmount) != entries.Sum(x => x.CreditAmount))
                    result.UnbalancedJournalCount++;
            }
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
