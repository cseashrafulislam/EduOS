using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Central institutional P&L detail is one read model over the authoritative
    // posted GL; source modules keep their own invoices, receipts and settlements.
    public async Task<ApiResponse<InstitutionalIncomeExpenseDetailPageDto>> GetInstitutionalIncomeExpenseDetailsAsync(
        DateOnly fromDate, DateOnly toDate, string category = "all", long? accountId = null,
        string? sourceType = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.ErrorResponse(
                "Tenant administrator access required for institution-wide income and expense history.", 403);
        category = (category ?? "all").Trim().ToLowerInvariant();
        sourceType = string.IsNullOrWhiteSpace(sourceType) ? null : sourceType.Trim();
        if (!Dates(fromDate, toDate) || category is not ("all" or "income" or "expense") ||
            accountId is <= 0 || sourceType?.Length > 100 || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.ErrorResponse(
                "Valid period, category, source, account and page size (1–100) required.", 400);

        if (accountId.HasValue && !await db.Accounts.AsNoTracking().AnyAsync(a =>
            a.TenantId == user.TenantId && !a.IsDeleted && a.Id == accountId.Value &&
            (a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense) &&
            !a.IsGroup && a.AllowsPosting && a.CurrencyCode == "BDT", ct))
            return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.ErrorResponse(
                "An active institutional income or expense posting account is required.", 404);

        // Checks historical deletion, currency, missing accounts, invalid lines and
        // per-journal double-entry integrity before showing ANY subset of the GL.
        var statement = await GetIncomeStatementAsync(fromDate, toDate, ct);
        if (!statement.Success || statement.Data == null)
            return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.ErrorResponse(statement.Message, statement.StatusCode);

        var lines = from line in Lines
            join journal in Posted on line.JournalId equals journal.Id
            join account in db.Accounts.AsNoTracking().Where(a =>
                a.TenantId == user.TenantId && !a.IsDeleted) on line.AccountId equals account.Id
            where journal.JournalDate >= fromDate && journal.JournalDate <= toDate &&
                (account.AccountType == AccountType.Income || account.AccountType == AccountType.Expense)
            select new
            {
                line.Id, line.LineNo, line.AccountId, LineCampusId = line.CampusId,
                LineDescription = line.Description, line.DebitAmount, line.CreditAmount,
                AccountCode = account.Code, AccountName = account.Name, account.AccountType,
                journal.JournalDate, journal.JournalNumber, journal.PublicId,
                JournalCampusId = journal.CampusId, journal.SourceType, journal.SourceId,
                journal.ReferenceNumber, journal.Description, journal.ReversalOfJournalId, journal.State
            };
        if (category == "income") lines = lines.Where(x => x.AccountType == AccountType.Income);
        if (category == "expense") lines = lines.Where(x => x.AccountType == AccountType.Expense);
        if (accountId.HasValue) lines = lines.Where(x => x.AccountId == accountId.Value);
        if (sourceType != null) lines = lines.Where(x => x.SourceType == sourceType);

        var totals = await lines.GroupBy(x => 1).Select(g => new
        {
            Count = g.Count(),
            Income = g.Sum(x => x.AccountType == AccountType.Income ? x.CreditAmount - x.DebitAmount : 0m),
            Expenses = g.Sum(x => x.AccountType == AccountType.Expense ? x.DebitAmount - x.CreditAmount : 0m)
        }).FirstOrDefaultAsync(ct);
        var totalIncome = totals?.Income ?? 0m;
        var totalExpenses = totals?.Expenses ?? 0m;
        if (category == "all" && !accountId.HasValue && sourceType == null &&
            (totalIncome != statement.Data.TotalIncome || totalExpenses != statement.Data.TotalExpenses))
            return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.ErrorResponse(
                "Central transaction detail does not reconcile with the institutional income statement.", 409);

        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue
            ? new List<InstitutionalIncomeExpenseLineDto>()
            : await lines.OrderByDescending(x => x.JournalDate)
                .ThenByDescending(x => x.JournalNumber).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new InstitutionalIncomeExpenseLineDto
                {
                    JournalReference = x.PublicId, JournalNumber = x.JournalNumber, Date = x.JournalDate,
                    CampusId = x.LineCampusId ?? x.JournalCampusId,
                    Category = x.AccountType == AccountType.Income ? "Income" : "Expense",
                    AccountCode = x.AccountCode, AccountName = x.AccountName,
                    SourceType = x.SourceType, SourceId = x.SourceId,
                    ReferenceNumber = x.ReferenceNumber, LineNo = x.LineNo,
                    Description = x.LineDescription ?? x.Description,
                    IsReversal = x.ReversalOfJournalId.HasValue,
                    IsReversedOriginal = x.State == JournalState.Reversed,
                    DebitAmount = x.DebitAmount, CreditAmount = x.CreditAmount,
                    Amount = x.AccountType == AccountType.Income
                        ? x.CreditAmount - x.DebitAmount : x.DebitAmount - x.CreditAmount
                }).ToListAsync(ct);

        return ApiResponse<InstitutionalIncomeExpenseDetailPageDto>.SuccessResponse(
            new InstitutionalIncomeExpenseDetailPageDto
            {
                FromDate = fromDate, ToDate = toDate, Category = category, AccountId = accountId,
                SourceType = sourceType, TotalIncome = totalIncome, TotalExpenses = totalExpenses,
                NetSurplus = totalIncome - totalExpenses,
                Entries = new PagedResult<InstitutionalIncomeExpenseLineDto>
                {
                    Items = rows, TotalCount = totals?.Count ?? 0, Page = page, PageSize = pageSize
                }
            });
    }
}
