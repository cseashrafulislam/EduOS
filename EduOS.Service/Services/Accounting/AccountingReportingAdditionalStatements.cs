using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // A day book records each posted journal once; its lines remain authoritative in the GL.
    // The page is of journals, never arbitrarily split balancing debit/credit legs.
    public async Task<ApiResponse<PostedDayBookDto>> GetPostedDayBookAsync(
        DateOnly fromDate, DateOnly toDate, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<PostedDayBookDto>.ErrorResponse("Tenant administrator access required for the institution-wide day book.", 403);
        if (!Dates(fromDate, toDate) || page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<PostedDayBookDto>.ErrorResponse("Valid reporting dates and page size (1–100) required.", 400);
        var historicalError = await HistoricalPostingErrorAsync(fromDate, toDate, null, ct);
        if (historicalError != null)
            return ApiResponse<PostedDayBookDto>.ErrorResponse(historicalError, 409);

        var journals = Posted.Where(j => j.JournalDate >= fromDate && j.JournalDate <= toDate);
        if (await journals.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<PostedDayBookDto>.ErrorResponse("Mixed-currency journals require an approved FX policy.", 409);
        if (await journals.AnyAsync(j => !Lines.Any(l => l.JournalId == j.Id), ct))
            return ApiResponse<PostedDayBookDto>.ErrorResponse("A posted journal has no lines.", 409);

        var lines = from line in Lines
            join journal in journals on line.JournalId equals journal.Id
            select line;
        if (await lines.AnyAsync(l => l.DebitAmount < 0m || l.CreditAmount < 0m ||
            (l.DebitAmount > 0m) == (l.CreditAmount > 0m), ct))
            return ApiResponse<PostedDayBookDto>.ErrorResponse("A posted journal has an invalid debit/credit line.", 409);
        if (await lines.AnyAsync(l => !db.Accounts.AsNoTracking().Any(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.Id == l.AccountId && a.CurrencyCode == "BDT" && !a.IsGroup && a.AllowsPosting), ct))
            return ApiResponse<PostedDayBookDto>.ErrorResponse("A posted line references an invalid or deleted posting account.", 409);

        var sums = from line in lines
            group line by line.JournalId into g
            select new { JournalId = g.Key, Count = g.Count(), Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount) };
        if (await sums.AnyAsync(x => x.Count < 2 || x.Debit <= 0m || x.Debit != x.Credit, ct))
            return ApiResponse<PostedDayBookDto>.ErrorResponse("An unbalanced posted journal prevents day book reporting.", 409);

        var totals = await lines.GroupBy(l => 1).Select(g => new
        {
            Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount)
        }).FirstOrDefaultAsync(ct);
        var totalCount = await journals.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var pageHeaders = offset > int.MaxValue
            ? new List<PostedDayBookRowDto>()
            : await journals.OrderBy(j => j.JournalDate).ThenBy(j => j.JournalNumber).ThenBy(j => j.Id)
                .Skip((int)offset).Take(pageSize).Select(j => new PostedDayBookRowDto
                {
                    JournalReference = j.PublicId, JournalNumber = j.JournalNumber, Date = j.JournalDate,
                    CampusId = j.CampusId, SourceType = j.SourceType, ReferenceNumber = j.ReferenceNumber,
                    Description = j.Description, IsReversal = j.ReversalOfJournalId.HasValue,
                    IsReversedOriginal = j.State == JournalState.Reversed
                }).ToListAsync(ct);
        // Use the immutable journal reference so the public DTO has no internal numeric ID.
        var pageReferences = pageHeaders.Select(j => j.JournalReference).ToArray();
        var pageSums = await (from j in journals
            join line in Lines on j.Id equals line.JournalId
            where pageReferences.Contains(j.PublicId)
            group line by j.PublicId into g
            select new { Reference = g.Key, Count = g.Count(), Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount) }).ToListAsync(ct);
        var byReference = pageSums.ToDictionary(x => x.Reference);
        foreach (var row in pageHeaders)
        {
            if (!byReference.TryGetValue(row.JournalReference, out var sum))
                return ApiResponse<PostedDayBookDto>.ErrorResponse("Journal lines changed during report generation; retry.", 409);
            row.LineCount = sum.Count;
            row.DebitAmount = sum.Debit;
            row.CreditAmount = sum.Credit;
        }
        return ApiResponse<PostedDayBookDto>.SuccessResponse(new PostedDayBookDto
        {
            FromDate = fromDate, ToDate = toDate,
            TotalDebit = totals?.Debit ?? 0m, TotalCredit = totals?.Credit ?? 0m,
            Journals = new PagedResult<PostedDayBookRowDto>
            {
                Items = pageHeaders, Page = page, PageSize = pageSize, TotalCount = totalCount
            }
        });
    }

    // Both columns call the existing independently reconciled financial statements;
    // absent accounts in one period are shown as zero, never silently omitted.
    public async Task<ApiResponse<ComparativeIncomeStatementDto>> GetComparativeIncomeStatementAsync(
        DateOnly fromDate, DateOnly toDate, DateOnly comparisonFrom, DateOnly comparisonTo,
        CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<ComparativeIncomeStatementDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (!Dates(fromDate, toDate) || !Dates(comparisonFrom, comparisonTo))
            return ApiResponse<ComparativeIncomeStatementDto>.ErrorResponse("Two valid periods (up to ten years each) required.", 400);
        var current = await GetIncomeStatementAsync(fromDate, toDate, ct);
        if (!current.Success || current.Data == null)
            return ApiResponse<ComparativeIncomeStatementDto>.ErrorResponse(current.Message, current.StatusCode);
        var previous = await GetIncomeStatementAsync(comparisonFrom, comparisonTo, ct);
        if (!previous.Success || previous.Data == null)
            return ApiResponse<ComparativeIncomeStatementDto>.ErrorResponse(previous.Message, previous.StatusCode);

        var rows = new Dictionary<long, ComparativeAccountRowDto>();
        foreach (var (label, currentRows, previousRows) in new[]
        {
            ("Income", current.Data.IncomeRows, previous.Data.IncomeRows),
            ("Expense", current.Data.ExpenseRows, previous.Data.ExpenseRows)
        })
        {
            foreach (var line in currentRows)
                rows[line.AccountId] = new ComparativeAccountRowDto
                {
                    AccountId = line.AccountId, AccountCode = line.AccountCode,
                    AccountName = line.AccountName, Section = label, CurrentAmount = line.Amount
                };
            foreach (var line in previousRows)
            {
                if (!rows.TryGetValue(line.AccountId, out var row))
                    rows[line.AccountId] = row = new ComparativeAccountRowDto
                    {
                        AccountId = line.AccountId, AccountCode = line.AccountCode,
                        AccountName = line.AccountName, Section = label
                    };
                row.ComparisonAmount = line.Amount;
            }
        }
        foreach (var row in rows.Values)
            row.Variance = row.CurrentAmount - row.ComparisonAmount;
        return ApiResponse<ComparativeIncomeStatementDto>.SuccessResponse(new ComparativeIncomeStatementDto
        {
            FromDate = fromDate, ToDate = toDate, ComparisonFrom = comparisonFrom,
            ComparisonTo = comparisonTo,
            Accounts = rows.Values.OrderBy(x => x.Section).ThenBy(x => x.AccountCode).ThenBy(x => x.AccountId).ToArray(),
            CurrentIncome = current.Data.TotalIncome, ComparisonIncome = previous.Data.TotalIncome,
            CurrentExpenses = current.Data.TotalExpenses, ComparisonExpenses = previous.Data.TotalExpenses,
            CurrentNetSurplus = current.Data.NetSurplus, ComparisonNetSurplus = previous.Data.NetSurplus
        });
    }

    public async Task<ApiResponse<ComparativeBalanceSheetDto>> GetComparativeBalanceSheetAsync(
        DateOnly asOfDate, DateOnly comparisonAsOfDate, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<ComparativeBalanceSheetDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (asOfDate == default || comparisonAsOfDate == default)
            return ApiResponse<ComparativeBalanceSheetDto>.ErrorResponse("Two valid as-of dates required.", 400);
        var current = await GetBalanceSheetAsync(asOfDate, ct);
        if (!current.Success || current.Data == null)
            return ApiResponse<ComparativeBalanceSheetDto>.ErrorResponse(current.Message, current.StatusCode);
        var previous = await GetBalanceSheetAsync(comparisonAsOfDate, ct);
        if (!previous.Success || previous.Data == null)
            return ApiResponse<ComparativeBalanceSheetDto>.ErrorResponse(previous.Message, previous.StatusCode);

        var rows = new Dictionary<long, ComparativeAccountRowDto>();
        foreach (var (label, currentRows, previousRows) in new[]
        {
            ("Asset", current.Data.AssetRows, previous.Data.AssetRows),
            ("Liability", current.Data.LiabilityRows, previous.Data.LiabilityRows),
            ("Equity", current.Data.EquityRows, previous.Data.EquityRows)
        })
        {
            foreach (var line in currentRows)
                rows[line.AccountId] = new ComparativeAccountRowDto
                {
                    AccountId = line.AccountId, AccountCode = line.AccountCode,
                    AccountName = line.AccountName, Section = label, CurrentAmount = line.Amount
                };
            foreach (var line in previousRows)
            {
                if (!rows.TryGetValue(line.AccountId, out var row))
                    rows[line.AccountId] = row = new ComparativeAccountRowDto
                    {
                        AccountId = line.AccountId, AccountCode = line.AccountCode,
                        AccountName = line.AccountName, Section = label
                    };
                row.ComparisonAmount = line.Amount;
            }
        }
        foreach (var row in rows.Values)
            row.Variance = row.CurrentAmount - row.ComparisonAmount;
        return ApiResponse<ComparativeBalanceSheetDto>.SuccessResponse(new ComparativeBalanceSheetDto
        {
            AsOfDate = asOfDate, ComparisonAsOfDate = comparisonAsOfDate,
            Accounts = rows.Values.OrderBy(x => x.Section).ThenBy(x => x.AccountCode).ThenBy(x => x.AccountId).ToArray(),
            CurrentAssets = current.Data.TotalAssets, ComparisonAssets = previous.Data.TotalAssets,
            CurrentLiabilities = current.Data.TotalLiabilities, ComparisonLiabilities = previous.Data.TotalLiabilities,
            CurrentEquity = current.Data.TotalEquity, ComparisonEquity = previous.Data.TotalEquity,
            CurrentUnclosedSurplus = current.Data.UnclosedNetSurplus,
            ComparisonUnclosedSurplus = previous.Data.UnclosedNetSurplus
        });
    }
}
