using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // The legacy trial-balance endpoint keeps its original full-list contract.
    // This read-only alternative pages in SQL and computes institution-wide
    // totals independently of the selected page and number of posting accounts.
    public async Task<ApiResponse<TrialBalancePageDto>> GetTrialBalancePageAsync(
        DateOnly asOfDate, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Tenant administrator access required for trial balance.", 403);
        if (asOfDate == default || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Valid as-of date and page size (1–100) required.", 400);

        var tenant = user.TenantId;
        var historyError = await HistoricalPostingErrorAsync(null, asOfDate, null, ct);
        if (historyError != null)
            return ApiResponse<TrialBalancePageDto>.ErrorResponse(historyError, 409);

        var journals = Posted.Where(j => j.JournalDate <= asOfDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Mixed-currency journals require an approved FX policy.", 409);
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("A posted journal has no lines.", 409);
        if (await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(l =>
            l.TenantId != tenant && journals.Any(j => j.Id == l.JournalId), ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Posted journal contains a foreign-institution line.", 409);

        var entries = from line in Lines
            join journal in journals on line.JournalId equals journal.Id
            select new { JournalId = journal.Id, line.AccountId, line.DebitAmount, line.CreditAmount };
        if (await entries.AnyAsync(x => x.DebitAmount < 0m || x.CreditAmount < 0m ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m), ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Invalid posted journal line.", 409);
        if (await entries.GroupBy(x => x.JournalId).Select(g => new
            {
                Count = g.Count(), Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount)
            }).AnyAsync(x => x.Count < 2 || x.Debit <= 0m || x.Debit != x.Credit, ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Unbalanced posted journal.", 409);

        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == tenant &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);
        if (await entries.AnyAsync(e => !accounts.Any(a => a.Id == e.AccountId), ct))
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Missing or invalid posting account.", 409);

        var balances = from e in entries
            join a in accounts on e.AccountId equals a.Id
            group e by new { a.Id, a.Code, a.Name } into g
            select new
            {
                AccountId = g.Key.Id, AccountCode = g.Key.Code, AccountName = g.Key.Name,
                Net = g.Sum(x => x.DebitAmount) - g.Sum(x => x.CreditAmount)
            };
        var totals = await balances.GroupBy(x => 1).Select(g => new
        {
            Count = g.Count(),
            Debit = g.Sum(x => x.Net > 0m ? x.Net : 0m),
            Credit = g.Sum(x => x.Net < 0m ? -x.Net : 0m)
        }).FirstOrDefaultAsync(ct);
        var debit = totals?.Debit ?? 0m;
        var credit = totals?.Credit ?? 0m;
        if (debit != credit)
            return ApiResponse<TrialBalancePageDto>.ErrorResponse("Trial balance does not reconcile.", 409);

        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue
            ? new List<TrialBalanceRowDto>()
            : await balances.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId)
                .Skip((int)offset).Take(pageSize).Select(x => new TrialBalanceRowDto
                {
                    AccountId = x.AccountId, AccountCode = x.AccountCode, AccountName = x.AccountName,
                    DebitBalance = x.Net > 0m ? x.Net : 0m,
                    CreditBalance = x.Net < 0m ? -x.Net : 0m
                }).ToListAsync(ct);
        return ApiResponse<TrialBalancePageDto>.SuccessResponse(new TrialBalancePageDto
        {
            AsOfDate = asOfDate, TotalDebitBalance = debit, TotalCreditBalance = credit,
            Accounts = new PagedResult<TrialBalanceRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = totals?.Count ?? 0, Items = rows
            }
        });
    }
}
