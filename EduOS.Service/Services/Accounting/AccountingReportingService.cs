using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService : IAccountingReportingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    public AccountingReportingService(EduOSDbContext db, ICurrentUserService user) { this.db = db; this.user = user; }
    private bool Allowed => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private IQueryable<Journal> Posted => db.Journals.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
        !x.IsDeleted && (x.State == JournalState.Posted || x.State == JournalState.Reversed));
    private IQueryable<JournalLine> Lines => db.JournalLines.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static bool Dates(DateOnly from, DateOnly to) => from != default && to >= from && to.DayNumber - from.DayNumber <= 3660;
    public async Task<ApiResponse<LedgerStatementDto>> GetLedgerStatementAsync(long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<LedgerStatementDto>.ErrorResponse("Institutional accounting access denied.", 403);
        if (accountId <= 0 || !Dates(fromDate, toDate))
            return ApiResponse<LedgerStatementDto>.ErrorResponse("Valid account and date range (up to ten years) required.");
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.Id == accountId, ct);
        if (account == null) return ApiResponse<LedgerStatementDto>.ErrorResponse("Account not found.", 404);
        var query = from l in Lines
            join j in Posted on l.JournalId equals j.Id
            where l.AccountId == accountId
            select new { l.Id, l.LineNo, l.DebitAmount, l.CreditAmount, l.Description,
                j.JournalDate, j.JournalNumber };
        var opening = await query.Where(x => x.JournalDate < fromDate)
            .Select(x => (decimal?)(x.DebitAmount - x.CreditAmount)).SumAsync(ct) ?? 0m;
        var range = query.Where(x => x.JournalDate >= fromDate && x.JournalDate <= toDate);
        if (await range.CountAsync(ct) > 5000)
            return ApiResponse<LedgerStatementDto>.ErrorResponse("More than 5,000 ledger lines; narrow the date range.", 422);
        var records = await range.OrderBy(x => x.JournalDate).ThenBy(x => x.JournalNumber)
            .ThenBy(x => x.LineNo).ThenBy(x => x.Id).ToListAsync(ct);
        var running = opening;
        var entries = new List<LedgerLineDto>(records.Count);
        foreach (var x in records)
        {
            running += x.DebitAmount - x.CreditAmount;
            entries.Add(new LedgerLineDto { Date = x.JournalDate, JournalNumber = x.JournalNumber,
                Description = x.Description, DebitAmount = x.DebitAmount,
                CreditAmount = x.CreditAmount, RunningBalance = running });
        }
        return ApiResponse<LedgerStatementDto>.SuccessResponse(new LedgerStatementDto
        {
            AccountId = accountId, FromDate = fromDate, ToDate = toDate,
            OpeningBalance = opening, ClosingBalance = running, Entries = entries
        });
    }
    public async Task<ApiResponse<IReadOnlyList<TrialBalanceRowDto>>> GetTrialBalanceAsync(DateOnly asOfDate, CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.ErrorResponse("Institutional accounting access denied.", 403);
        if (asOfDate == default) return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.ErrorResponse("Valid as-of date required.");
        var balances = from l in Lines
            join j in Posted on l.JournalId equals j.Id
            where j.JournalDate <= asOfDate
            group l by l.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) };
        var query = from b in balances
            join a in db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
                on b.AccountId equals a.Id
            orderby a.Code, a.Id
            select new { b.AccountId, a.Code, a.Name, b.Debit, b.Credit };
        if (await query.CountAsync(ct) > 5000)
            return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.ErrorResponse("More than 5,000 accounts; reporting export integration required.", 422);
        var groupedAccountCount = await balances.CountAsync(ct);
        var rows = await query.ToListAsync(ct);
        if (rows.Count != groupedAccountCount)
            return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.ErrorResponse("Posted ledger contains missing or deleted account references.", 409);
        var result = rows.Select(x => new TrialBalanceRowDto
        {
            AccountId = x.AccountId, AccountCode = x.Code, AccountName = x.Name,
            DebitBalance = Math.Max(0m, x.Debit - x.Credit),
            CreditBalance = Math.Max(0m, x.Credit - x.Debit)
        }).ToArray();
        if (result.Sum(x => x.DebitBalance) != result.Sum(x => x.CreditBalance))
            return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.ErrorResponse("Trial balance is not balanced; investigate posted journals.", 409);
        return ApiResponse<IReadOnlyList<TrialBalanceRowDto>>.SuccessResponse(result);
    }

    // Tenant-wide institutional statement. Until campus-scoped accounting reads are formalized,
    // only TenantAdmin can see the combined result; no SaaS subscription data is queried.
    public async Task<ApiResponse<IncomeStatementDto>> GetIncomeStatementAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || !user.IsTenantAdmin)
            return ApiResponse<IncomeStatementDto>.ErrorResponse("Tenant administrator access required for the institution-wide income statement.", 403);
        if (!Dates(fromDate, toDate))
            return ApiResponse<IncomeStatementDto>.ErrorResponse("Valid date range (up to ten years) required.", 400);
        var journals = Posted.Where(j => j.JournalDate >= fromDate && j.JournalDate <= toDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<IncomeStatementDto>.ErrorResponse("Mixed-currency journals cannot be combined without an approved FX policy.", 409);
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return ApiResponse<IncomeStatementDto>.ErrorResponse("A posted journal has no lines; reconcile the ledger first.", 409);
        var entries = from l in Lines
            join j in journals on l.JournalId equals j.Id
            select new { j.Id, l.AccountId, l.DebitAmount, l.CreditAmount };
        var unbalanced = await (from l in entries
            group l by l.Id into g
            select new { Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .AnyAsync(x => x.Debit != x.Credit, ct);
        if (unbalanced)
            return ApiResponse<IncomeStatementDto>.ErrorResponse("An unbalanced posted journal prevents financial reporting.", 409);
        var balances = await (from l in entries
            group l by l.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .ToListAsync(ct);
        if (balances.Count > 5000)
            return ApiResponse<IncomeStatementDto>.ErrorResponse("More than 5,000 posting accounts; reporting export integration required.", 422);
        var accountIds = balances.Select(x => x.AccountId).ToArray();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.TenantId == user.TenantId && !a.IsDeleted && accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.CurrencyCode }).ToListAsync(ct);
        if (accounts.Count != balances.Count)
            return ApiResponse<IncomeStatementDto>.ErrorResponse("Posted ledger contains missing or deleted account references.", 409);
        if (accounts.Any(a => a.CurrencyCode != "BDT"))
            return ApiResponse<IncomeStatementDto>.ErrorResponse("A posting account uses a different currency.", 409);
        var byId = accounts.ToDictionary(a => a.Id);
        var income = new List<IncomeStatementRowDto>();
        var expenses = new List<IncomeStatementRowDto>();
        foreach (var balance in balances.OrderBy(x => byId[x.AccountId].Code).ThenBy(x => x.AccountId))
        {
            var account = byId[balance.AccountId];
            if (account.AccountType != AccountType.Income && account.AccountType != AccountType.Expense) continue;
            var amount = account.AccountType == AccountType.Income
                ? balance.Credit - balance.Debit : balance.Debit - balance.Credit;
            var row = new IncomeStatementRowDto
            {
                AccountId = account.Id, AccountCode = account.Code, AccountName = account.Name, Amount = amount
            };
            if (account.AccountType == AccountType.Income) income.Add(row);
            else expenses.Add(row);
        }
        var totalIncome = income.Sum(x => x.Amount);
        var totalExpenses = expenses.Sum(x => x.Amount);
        return ApiResponse<IncomeStatementDto>.SuccessResponse(new IncomeStatementDto
        {
            FromDate = fromDate, ToDate = toDate, IncomeRows = income, ExpenseRows = expenses,
            TotalIncome = totalIncome, TotalExpenses = totalExpenses, NetSurplus = totalIncome - totalExpenses
        });
    }

    // Lifetime ledger balances as of the date, including compensating reversal journals.
    // This does not assume an accounting year-end: posted closing entries move surplus into equity.
    public async Task<ApiResponse<BalanceSheetDto>> GetBalanceSheetAsync(DateOnly asOfDate, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || !user.IsTenantAdmin)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Tenant administrator access required for the institution-wide balance sheet.", 403);
        if (asOfDate == default)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Valid as-of date required.", 400);
        var journals = Posted.Where(j => j.JournalDate <= asOfDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Mixed-currency journals require an approved FX policy.", 409);
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return ApiResponse<BalanceSheetDto>.ErrorResponse("A posted journal has no lines; reconcile the ledger first.", 409);
        var entries = from l in Lines
            join j in journals on l.JournalId equals j.Id
            select new { JournalId = j.Id, l.AccountId, l.DebitAmount, l.CreditAmount };
        if (await entries.AnyAsync(l => l.DebitAmount < 0m || l.CreditAmount < 0m ||
            (l.DebitAmount > 0m && l.CreditAmount > 0m) ||
            (l.DebitAmount == 0m && l.CreditAmount == 0m), ct))
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Invalid debit/credit journal line prevents reporting.", 409);
        var unbalanced = await (from l in entries
            group l by l.JournalId into g
            select new { Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .AnyAsync(x => x.Debit != x.Credit, ct);
        if (unbalanced)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("An unbalanced posted journal prevents financial reporting.", 409);
        var balances = await (from l in entries
            group l by l.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .ToListAsync(ct);
        if (balances.Count > 5000)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("More than 5,000 posting accounts; reporting export integration required.", 422);
        var accountIds = balances.Select(x => x.AccountId).ToArray();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.TenantId == user.TenantId && !a.IsDeleted && accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.CurrencyCode, a.IsGroup, a.AllowsPosting })
            .ToListAsync(ct);
        if (accounts.Count != balances.Count)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Posted ledger contains missing or deleted account references.", 409);
        if (accounts.Any(a => a.CurrencyCode != "BDT" || a.IsGroup || !a.AllowsPosting))
            return ApiResponse<BalanceSheetDto>.ErrorResponse("A posting account has invalid currency or posting configuration.", 409);
        var byId = accounts.ToDictionary(a => a.Id);
        var assets = new List<BalanceSheetRowDto>();
        var liabilities = new List<BalanceSheetRowDto>();
        var equity = new List<BalanceSheetRowDto>();
        decimal unclosedSurplus = 0m;
        foreach (var balance in balances.OrderBy(x => byId[x.AccountId].Code).ThenBy(x => x.AccountId))
        {
            var account = byId[balance.AccountId];
            if (account.AccountType == AccountType.Income)
            {
                unclosedSurplus += balance.Credit - balance.Debit;
                continue;
            }
            if (account.AccountType == AccountType.Expense)
            {
                unclosedSurplus -= balance.Debit - balance.Credit;
                continue;
            }
            if (account.AccountType != AccountType.Asset &&
                account.AccountType != AccountType.Liability &&
                account.AccountType != AccountType.Equity)
                return ApiResponse<BalanceSheetDto>.ErrorResponse("An account has an unsupported accounting classification.", 409);
            var amount = account.AccountType == AccountType.Asset
                ? balance.Debit - balance.Credit : balance.Credit - balance.Debit;
            var row = new BalanceSheetRowDto
            {
                AccountId = account.Id, AccountCode = account.Code, AccountName = account.Name, Amount = amount
            };
            if (account.AccountType == AccountType.Asset) assets.Add(row);
            else if (account.AccountType == AccountType.Liability) liabilities.Add(row);
            else equity.Add(row);
        }
        var totalAssets = assets.Sum(x => x.Amount);
        var totalLiabilities = liabilities.Sum(x => x.Amount);
        var totalEquity = equity.Sum(x => x.Amount);
        var totalLiabilitiesAndEquity = totalLiabilities + totalEquity + unclosedSurplus;
        if (totalAssets != totalLiabilitiesAndEquity)
            return ApiResponse<BalanceSheetDto>.ErrorResponse("Balance sheet does not reconcile; investigate account classification and posted journals.", 409);
        return ApiResponse<BalanceSheetDto>.SuccessResponse(new BalanceSheetDto
        {
            AsOfDate = asOfDate, AssetRows = assets, LiabilityRows = liabilities, EquityRows = equity,
            TotalAssets = totalAssets, TotalLiabilities = totalLiabilities, TotalEquity = totalEquity,
            UnclosedNetSurplus = unclosedSurplus, TotalLiabilitiesAndEquity = totalLiabilitiesAndEquity
        });
    }

}
