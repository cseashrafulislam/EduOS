using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Stable journal-line identities are needed for subsequent explicit matching.
    // Server-side tenant/role/campus/ledger validation precedes every query.
    // This endpoint is evidence only: it never clears, approves or changes postings.
    public async Task<ApiResponse<BankReconciliationCandidatePageDto>> GetBankReconciliationCandidatesAsync(
        long bankAccountId, DateOnly fromDate, DateOnly toDate, string? referencePrefix,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Tenant administrator access required.", 403);
        if (bankAccountId <= 0 || !Dates(fromDate, toDate) || page < 1 || page > 10000 ||
            pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > 1000000)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Valid bank, period and bounded page (1-100 rows) required.", 400);

        referencePrefix = referencePrefix?.Trim();
        if (referencePrefix?.Length > 100 || referencePrefix?.Any(char.IsControl) == true)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Reference prefix must be 100 printable characters or fewer.", 400);

        var banks = db.BankAccounts.AsNoTracking()
            .Where(b => b.TenantId == user.TenantId && !b.IsDeleted);
        var bank = await banks.FirstOrDefaultAsync(b => b.Id == bankAccountId, ct);
        if (bank == null)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse("Bank account not found.", 404);
        if (bank.CurrencyCode != "BDT" ||
            await banks.CountAsync(b => b.AccountId == bank.AccountId, ct) != 1)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Bank currency or unique ledger mapping is invalid.", 409);

        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == user.TenantId && !a.IsDeleted && a.Id == bank.AccountId, ct);
        if (account == null || account.AccountType != AccountType.Asset ||
            account.IsGroup || !account.AllowsPosting || account.CurrencyCode != "BDT")
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Bank ledger is not a valid BDT posting asset.", 409);

        var historyError = await HistoricalPostingErrorAsync(null, toDate, new[] { bank.AccountId }, ct);
        if (historyError != null)
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(historyError, 409);

        var affected = Posted.Where(j => j.JournalDate <= toDate &&
            Lines.Any(l => l.JournalId == j.Id && l.AccountId == bank.AccountId));
        var affectedIds = affected.Select(j => j.Id);
        var allLines = Lines.Where(l => affectedIds.Contains(l.JournalId));
        if (await affected.AnyAsync(j => j.CurrencyCode != "BDT", ct) ||
            await allLines.AnyAsync(l => l.DebitAmount < 0m || l.CreditAmount < 0m ||
                (l.DebitAmount > 0m && l.CreditAmount > 0m) ||
                (l.DebitAmount == 0m && l.CreditAmount == 0m), ct) ||
            await (from l in allLines group l by l.JournalId into g
                select new { Debit = g.Sum(x => x.DebitAmount),
                    Credit = g.Sum(x => x.CreditAmount) })
                .AnyAsync(x => x.Debit != x.Credit, ct) ||
            await allLines.AnyAsync(l => !db.Accounts.AsNoTracking().Any(a =>
                a.TenantId == user.TenantId && !a.IsDeleted && a.Id == l.AccountId), ct))
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Bank posting integrity failed; reconcile journals before matching.", 409);

        if (bank.CampusId.HasValue &&
            await (from l in Lines join j in affected on l.JournalId equals j.Id
                where l.AccountId == bank.AccountId &&
                    (l.CampusId ?? j.CampusId) != bank.CampusId.Value
                select l.Id).AnyAsync(ct))
            return ApiResponse<BankReconciliationCandidatePageDto>.ErrorResponse(
                "Bank posting campus attribution is inconsistent.", 409);

        var query = from l in Lines
            join j in affected on l.JournalId equals j.Id
            where l.AccountId == bank.AccountId &&
                j.JournalDate >= fromDate && j.JournalDate <= toDate
            select new
            {
                l.Id, l.JournalId, l.LineNo, l.RowVersion,
                l.CampusId, l.DebitAmount, l.CreditAmount,
                j.JournalNumber, j.JournalDate, j.ReferenceNumber,
                j.SourceType, JournalCampusId = j.CampusId
            };
        if (!string.IsNullOrEmpty(referencePrefix))
            query = query.Where(x => x.ReferenceNumber != null &&
                x.ReferenceNumber.StartsWith(referencePrefix));

        var total = await query.CountAsync(ct);
        var totals = await query.GroupBy(x => 1).Select(g => new
        {
            Deposit = g.Sum(x => x.DebitAmount),
            Withdrawal = g.Sum(x => x.CreditAmount)
        }).FirstOrDefaultAsync(ct);
        var pageRows = await query.OrderBy(x => x.JournalDate)
            .ThenBy(x => x.JournalNumber).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var rows = pageRows.Select(x => new BankReconciliationCandidateDto
        {
            JournalLineId = x.Id, JournalId = x.JournalId,
            JournalNumber = x.JournalNumber, Date = x.JournalDate,
            Reference = x.ReferenceNumber, SourceType = x.SourceType,
            CampusId = x.CampusId ?? x.JournalCampusId,
            Deposit = x.DebitAmount, Withdrawal = x.CreditAmount,
            JournalLineVersion = Convert.ToBase64String(x.RowVersion)
        }).ToArray();
        return ApiResponse<BankReconciliationCandidatePageDto>.SuccessResponse(
            new BankReconciliationCandidatePageDto
            {
                BankAccountId = bank.Id, BankName = bank.BankName,
                FromDate = fromDate, ToDate = toDate,
                ReferencePrefix = referencePrefix, Page = page, PageSize = pageSize,
                TotalCount = total, TotalDeposits = totals?.Deposit ?? 0m,
                TotalWithdrawals = totals?.Withdrawal ?? 0m, Rows = rows
            });
    }
}
