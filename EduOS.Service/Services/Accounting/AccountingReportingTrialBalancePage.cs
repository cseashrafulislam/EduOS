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

        var error = await ValidateInstitutionalAsOfPostedGLAsync(asOfDate, ct);
        if (error != null)
            return ApiResponse<TrialBalancePageDto>.ErrorResponse(error, 409);

        var entries = from line in Lines
            join journal in Posted.Where(j => j.JournalDate <= asOfDate) on line.JournalId equals journal.Id
            select new { line.AccountId, line.DebitAmount, line.CreditAmount };
        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);

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

    // Shared strict as-of validation for the new paged Trial Balance and Balance
    // Sheet. Existing full-list report contracts remain unchanged.
    private async Task<string?> ValidateInstitutionalAsOfPostedGLAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var historical = await HistoricalPostingErrorAsync(null, asOfDate, null, ct);
        if (historical != null) return historical;

        var journals = Posted.Where(j => j.JournalDate <= asOfDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return "Mixed-currency journals require an approved FX policy.";
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return "A posted journal has no lines.";
        if (await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(l =>
            l.TenantId != user.TenantId && journals.Any(j => j.Id == l.JournalId), ct))
            return "Posted journal contains a foreign-institution line.";

        var entries = from line in Lines
            join journal in journals on line.JournalId equals journal.Id
            select new { JournalId = journal.Id, line.AccountId, line.DebitAmount, line.CreditAmount };
        if (await entries.AnyAsync(x => x.DebitAmount < 0m || x.CreditAmount < 0m ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m), ct))
            return "Invalid posted journal line.";
        if (await entries.GroupBy(x => x.JournalId).Select(g => new
            {
                Count = g.Count(), Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount)
            }).AnyAsync(x => x.Count < 2 || x.Debit <= 0m || x.Debit != x.Credit, ct))
            return "Unbalanced posted journal.";

        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);
        return await entries.AnyAsync(e => !accounts.Any(a => a.Id == e.AccountId), ct)
            ? "Missing or invalid posting account." : null;
    }
}
