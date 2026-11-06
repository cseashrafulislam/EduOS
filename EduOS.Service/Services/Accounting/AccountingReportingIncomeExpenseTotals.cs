using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // The canonical posted GL is the only financial authority. This bounded
    // aggregate does not load 5,000+ posting-account rows just to show a total.
    // It is intentionally separate from the legacy full-detail statement API.
    public async Task<ApiResponse<InstitutionalIncomeExpenseTotalsDto>> GetInstitutionalIncomeExpenseTotalsAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Tenant administrator access required for institutional finance totals.", 403);
        if (!Dates(fromDate, toDate))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Valid date range (up to ten years) required.", 400);

        var historyError = await HistoricalPostingErrorAsync(fromDate, toDate, null, ct);
        if (historyError != null)
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(historyError, 409);

        var tenant = user.TenantId;
        var journals = Posted.Where(j => j.JournalDate >= fromDate && j.JournalDate <= toDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Mixed-currency journals require an approved FX policy.", 409);
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "A posted journal has no lines.", 409);

        // A cross-tenant line on a tenant-owned journal is corruption; do not
        // silently hide it behind the tenant query filter and return false totals.
        if (await db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(l =>
            l.TenantId != tenant && journals.Any(j => j.Id == l.JournalId), ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Posted journal contains a foreign-institution line.", 409);

        var entries = from l in Lines
            join j in journals on l.JournalId equals j.Id
            select new { j.Id, l.AccountId, l.DebitAmount, l.CreditAmount };
        if (await entries.AnyAsync(l => l.DebitAmount < 0m || l.CreditAmount < 0m ||
            (l.DebitAmount > 0m) == (l.CreditAmount > 0m), ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Invalid debit/credit journal line prevents reporting.", 409);
        if (await entries.GroupBy(x => x.Id).Select(g => new
            {
                Count = g.Count(), Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount)
            }).AnyAsync(x => x.Count < 2 || x.Debit <= 0m || x.Debit != x.Credit, ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "An unbalanced posted journal prevents reporting.", 409);

        var accounts = db.Accounts.AsNoTracking().Where(a => a.TenantId == tenant &&
            !a.IsDeleted && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting);
        if (await entries.AnyAsync(l => !accounts.Any(a => a.Id == l.AccountId), ct))
            return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.ErrorResponse(
                "Posted journal references a missing or invalid posting account.", 409);

        var totals = await (from l in entries
            join a in accounts on l.AccountId equals a.Id
            select new { a.AccountType, l.DebitAmount, l.CreditAmount })
            .GroupBy(x => 1)
            .Select(g => new
            {
                Income = g.Sum(x => x.AccountType == AccountType.Income
                    ? x.CreditAmount - x.DebitAmount : 0m),
                Expense = g.Sum(x => x.AccountType == AccountType.Expense
                    ? x.DebitAmount - x.CreditAmount : 0m)
            }).FirstOrDefaultAsync(ct);
        var income = totals?.Income ?? 0m;
        var expense = totals?.Expense ?? 0m;
        return ApiResponse<InstitutionalIncomeExpenseTotalsDto>.SuccessResponse(
            new InstitutionalIncomeExpenseTotalsDto
            {
                FromDate = fromDate, ToDate = toDate,
                TotalIncome = income, TotalExpenses = expense,
                NetSurplus = income - expense
            });
    }
}
