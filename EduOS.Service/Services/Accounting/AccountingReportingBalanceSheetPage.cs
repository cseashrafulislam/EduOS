using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Preserve the legacy full-list route. This SQL-paged read model provides
    // signed natural balances and whole-institution totals even at high account
    // cardinality; it never assumes a fiscal closing journal was posted.
    public async Task<ApiResponse<BalanceSheetPageDto>> GetBalanceSheetPageAsync(
        DateOnly asOfDate, string category = "all", int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BalanceSheetPageDto>.ErrorResponse("Tenant administrator access required for balance sheet.", 403);
        category = (category ?? "all").Trim().ToLowerInvariant();
        if (asOfDate == default || category is not ("all" or "asset" or "liability" or "equity") ||
            page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<BalanceSheetPageDto>.ErrorResponse("Valid as-of date, category and page size (1–100) required.", 400);

        var error = await ValidateInstitutionalAsOfPostedGLAsync(asOfDate, ct);
        if (error != null)
            return ApiResponse<BalanceSheetPageDto>.ErrorResponse(error, 409);

        var entries = from line in Lines
            join journal in Posted.Where(j => j.JournalDate <= asOfDate) on line.JournalId equals journal.Id
            select new { line.AccountId, line.DebitAmount, line.CreditAmount };
        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);
        var balances = from e in entries
            join a in accounts on e.AccountId equals a.Id
            group e by new { a.Id, a.Code, a.Name, a.AccountType } into g
            select new
            {
                AccountId = g.Key.Id, AccountCode = g.Key.Code, AccountName = g.Key.Name,
                g.Key.AccountType, Net = g.Sum(x => x.DebitAmount) - g.Sum(x => x.CreditAmount)
            };
        if (await balances.AnyAsync(x => x.AccountType != AccountType.Asset &&
            x.AccountType != AccountType.Liability && x.AccountType != AccountType.Equity &&
            x.AccountType != AccountType.Income && x.AccountType != AccountType.Expense, ct))
            return ApiResponse<BalanceSheetPageDto>.ErrorResponse("Unsupported account classification.", 409);

        var totals = await balances.GroupBy(x => 1).Select(g => new
        {
            Assets = g.Sum(x => x.AccountType == AccountType.Asset ? x.Net : 0m),
            Liabilities = g.Sum(x => x.AccountType == AccountType.Liability ? -x.Net : 0m),
            Equity = g.Sum(x => x.AccountType == AccountType.Equity ? -x.Net : 0m),
            Surplus = g.Sum(x => x.AccountType == AccountType.Income ||
                x.AccountType == AccountType.Expense ? -x.Net : 0m)
        }).FirstOrDefaultAsync(ct);
        var assets = totals?.Assets ?? 0m;
        var liabilities = totals?.Liabilities ?? 0m;
        var equity = totals?.Equity ?? 0m;
        var surplus = totals?.Surplus ?? 0m;
        if (assets != liabilities + equity + surplus)
            return ApiResponse<BalanceSheetPageDto>.ErrorResponse("Balance sheet does not reconcile.", 409);

        var rows = balances.Where(x => x.AccountType == AccountType.Asset ||
            x.AccountType == AccountType.Liability || x.AccountType == AccountType.Equity);
        if (category == "asset") rows = rows.Where(x => x.AccountType == AccountType.Asset);
        if (category == "liability") rows = rows.Where(x => x.AccountType == AccountType.Liability);
        if (category == "equity") rows = rows.Where(x => x.AccountType == AccountType.Equity);

        var count = await rows.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var items = offset > int.MaxValue
            ? new List<BalanceSheetRowDto>()
            : await rows.OrderBy(x => x.AccountCode).ThenBy(x => x.AccountId)
                .Skip((int)offset).Take(pageSize).Select(x => new BalanceSheetRowDto
                {
                    AccountId = x.AccountId, AccountCode = x.AccountCode, AccountName = x.AccountName,
                    Amount = x.AccountType == AccountType.Asset ? x.Net : -x.Net
                }).ToListAsync(ct);
        return ApiResponse<BalanceSheetPageDto>.SuccessResponse(new BalanceSheetPageDto
        {
            AsOfDate = asOfDate, Category = category, TotalAssets = assets,
            TotalLiabilities = liabilities, TotalEquity = equity,
            UnclosedNetSurplus = surplus, TotalLiabilitiesAndEquity = liabilities + equity + surplus,
            Accounts = new PagedResult<BalanceSheetRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = items
            }
        });
    }
}
