using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Full-period posted activity with a bounded SQL page. This does not alter the existing
    // historical ledger endpoint or mistake a page subtotal for an institution balance.
    public async Task<ApiResponse<LedgerActivityPageDto>> GetLedgerActivityAsync(
        long accountId, DateOnly fromDate, DateOnly toDate, int page = 1,
        int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse(
                "Tenant administrator access required for institution-wide journal activity.", 403);
        if (accountId <= 0 || !Dates(fromDate, toDate) ||
            page < 1 || pageSize is < 1 or > 100)
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse(
                "Valid account, period and page size (1–100) required.", 400);

        var tenant = user.TenantId;
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == tenant && !a.IsDeleted && a.Id == accountId, ct);
        if (account == null)
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse("Account not found.", 404);
        if (account.CurrencyCode != "BDT" || account.IsGroup || !account.AllowsPosting)
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse(
                "Select a valid BDT posting account.", 409);

        var historyError = await HistoricalPostingErrorAsync(null, toDate, new[] { accountId }, ct);
        if (historyError != null)
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse(historyError, 409);

        var movements = from line in Lines
            join journal in Posted on line.JournalId equals journal.Id
            where line.TenantId == tenant && journal.TenantId == tenant &&
                  line.AccountId == accountId && journal.JournalDate <= toDate
            select new
            {
                line.Id, line.LineNo, line.DebitAmount, line.CreditAmount,
                LineDescription = line.Description, LineCampusId = line.CampusId,
                journal.PublicId, journal.JournalDate, journal.JournalNumber,
                journal.ReferenceNumber, journal.SourceType, journal.CurrencyCode,
                journal.CampusId, journal.Description, journal.ReversalOfJournalId,
                journal.State
            };
        if (await movements.AnyAsync(x => x.CurrencyCode != "BDT" ||
            x.DebitAmount < 0m || x.CreditAmount < 0m ||
            (x.DebitAmount > 0m) == (x.CreditAmount > 0m), ct))
            return ApiResponse<LedgerActivityPageDto>.ErrorResponse(
                "Invalid posted bank/ledger movement prevents audit reporting.", 409);

        var opening = await movements.Where(x => x.JournalDate < fromDate)
            .Select(x => (decimal?)(x.DebitAmount - x.CreditAmount)).SumAsync(ct) ?? 0m;
        var period = movements.Where(x => x.JournalDate >= fromDate);
        var summary = await period.GroupBy(x => 1).Select(g => new
        {
            Count = g.Count(),
            Debit = g.Sum(x => x.DebitAmount),
            Credit = g.Sum(x => x.CreditAmount)
        }).FirstOrDefaultAsync(ct);
        var count = summary?.Count ?? 0;
        var debit = summary?.Debit ?? 0m;
        var credit = summary?.Credit ?? 0m;
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue
            ? new List<LedgerActivityRowDto>()
            : await period.OrderBy(x => x.JournalDate).ThenBy(x => x.JournalNumber)
                .ThenBy(x => x.LineNo).ThenBy(x => x.Id)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new LedgerActivityRowDto
                {
                    Date = x.JournalDate, JournalReference = x.PublicId,
                    JournalNumber = x.JournalNumber, LineNo = x.LineNo,
                    CampusId = x.LineCampusId ?? x.CampusId,
                    SourceType = x.SourceType, ReferenceNumber = x.ReferenceNumber,
                    Description = x.LineDescription ?? x.Description,
                    IsReversal = x.ReversalOfJournalId.HasValue,
                    IsReversedOriginal = x.State == JournalState.Reversed,
                    DebitAmount = x.DebitAmount, CreditAmount = x.CreditAmount
                }).ToListAsync(ct);

        return ApiResponse<LedgerActivityPageDto>.SuccessResponse(new LedgerActivityPageDto
        {
            AccountId = account.Id, AccountCode = account.Code, AccountName = account.Name,
            FromDate = fromDate, ToDate = toDate,
            OpeningBalance = opening, TotalDebit = debit, TotalCredit = credit,
            ClosingBalance = opening + debit - credit,
            Entries = new PagedResult<LedgerActivityRowDto>
            {
                Items = rows, TotalCount = count, Page = page, PageSize = pageSize
            }
        });
    }
}
