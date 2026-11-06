using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Keep the existing full-list report compatible. For large charts of accounts,
    // aggregate in SQL and return a deterministic bounded page without a 5k cap.
    public async Task<ApiResponse<IncomeStatementPageDto>> GetIncomeStatementPageAsync(
        DateOnly fromDate, DateOnly toDate, string category = "all", int page = 1,
        int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<IncomeStatementPageDto>.ErrorResponse(
                "Tenant administrator access required for the income statement.", 403);
        category = (category ?? "all").Trim().ToLowerInvariant();
        if (!Dates(fromDate, toDate) || category is not ("all" or "income" or "expense") ||
            page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<IncomeStatementPageDto>.ErrorResponse(
                "Valid period, category and page size (1–100) required.", 400);

        // The shared strict validator also checks deleted history, foreign-tenant
        // lines, journal balance/currency and posting-account validity. Do not
        // present a page that could conceal a corrupt journal outside that page.
        var totals = await GetInstitutionalIncomeExpenseTotalsAsync(fromDate, toDate, ct);
        if (!totals.Success || totals.Data == null)
            return ApiResponse<IncomeStatementPageDto>.ErrorResponse(totals.Message, totals.StatusCode);

        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);
        var balances = from line in Lines
            join journal in Posted.Where(j => j.JournalDate >= fromDate && j.JournalDate <= toDate)
                on line.JournalId equals journal.Id
            join account in accounts on line.AccountId equals account.Id
            where account.AccountType == AccountType.Income || account.AccountType == AccountType.Expense
            group line by new { account.Id, account.Code, account.Name, account.AccountType } into g
            select new
            {
                AccountId = g.Key.Id, AccountCode = g.Key.Code, AccountName = g.Key.Name,
                g.Key.AccountType,
                Amount = g.Sum(x => x.CreditAmount - x.DebitAmount)
            };

        var statementTotals = await balances.GroupBy(x => 1).Select(g => new
        {
            Income = g.Sum(x => x.AccountType == AccountType.Income ? x.Amount : 0m),
            Expense = g.Sum(x => x.AccountType == AccountType.Expense ? -x.Amount : 0m)
        }).FirstOrDefaultAsync(ct);
        if ((statementTotals?.Income ?? 0m) != totals.Data.TotalIncome ||
            (statementTotals?.Expense ?? 0m) != totals.Data.TotalExpenses)
            return ApiResponse<IncomeStatementPageDto>.ErrorResponse(
                "Account breakdown does not reconcile with posted GL totals.", 409);

        var selected = balances;
        if (category == "income") selected = selected.Where(x => x.AccountType == AccountType.Income);
        if (category == "expense") selected = selected.Where(x => x.AccountType == AccountType.Expense);
        var count = await selected.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue
            ? new List<IncomeStatementAccountRowDto>()
            : await selected.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new IncomeStatementAccountRowDto
                {
                    AccountId = x.AccountId, AccountCode = x.AccountCode,
                    AccountName = x.AccountName,
                    Category = x.AccountType == AccountType.Income ? "Income" : "Expense",
                    Amount = x.AccountType == AccountType.Income ? x.Amount : -x.Amount
                }).ToListAsync(ct);

        return ApiResponse<IncomeStatementPageDto>.SuccessResponse(new IncomeStatementPageDto
        {
            FromDate = fromDate, ToDate = toDate, Category = category,
            TotalIncome = totals.Data.TotalIncome, TotalExpenses = totals.Data.TotalExpenses,
            NetSurplus = totals.Data.NetSurplus,
            Accounts = new PagedResult<IncomeStatementAccountRowDto>
            {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            }
        });
    }
}
