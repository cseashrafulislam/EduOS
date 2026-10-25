using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed class AccountingReportingService : IAccountingReportingService
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
}
