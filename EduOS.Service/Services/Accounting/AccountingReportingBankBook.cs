using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // A bank book is a verified ledger movement statement, not a classified cash-flow statement.
    // No cash-account guessing or unsupported operating/investing/financing categorization.
    public async Task<ApiResponse<BankBookDto>> GetBankBookAsync(
        long bankAccountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || !user.IsTenantAdmin)
            return ApiResponse<BankBookDto>.ErrorResponse("Tenant administrator access required for the institution-wide bank book.", 403);
        if (bankAccountId <= 0 || !Dates(fromDate, toDate))
            return ApiResponse<BankBookDto>.ErrorResponse("Valid bank account and date range (up to ten years) required.", 400);

        var banks = db.BankAccounts.AsNoTracking().Where(b => b.TenantId == user.TenantId && !b.IsDeleted);
        var bank = await banks.FirstOrDefaultAsync(b => b.Id == bankAccountId, ct);
        if (bank == null) return ApiResponse<BankBookDto>.ErrorResponse("Bank account not found.", 404);
        if (bank.CurrencyCode != "BDT")
            return ApiResponse<BankBookDto>.ErrorResponse("Bank book requires a BDT bank account; FX conversion is not configured.", 409);
        if (await banks.CountAsync(b => b.AccountId == bank.AccountId, ct) != 1)
            return ApiResponse<BankBookDto>.ErrorResponse("Several bank accounts share one ledger; bank movements cannot be attributed safely.", 409);

        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TenantId == user.TenantId && !a.IsDeleted && a.Id == bank.AccountId, ct);
        if (account == null || account.AccountType != AccountType.Asset ||
            account.IsGroup || !account.AllowsPosting || account.CurrencyCode != "BDT")
            return ApiResponse<BankBookDto>.ErrorResponse("Bank mapping does not reference a valid BDT posting asset account.", 409);

        var affected = Posted.Where(j => j.JournalDate <= toDate &&
            Lines.Any(l => l.JournalId == j.Id && l.AccountId == bank.AccountId));
        if (await affected.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<BankBookDto>.ErrorResponse("A bank journal uses a different currency.", 409);
        var affectedIds = affected.Select(j => j.Id);
        var allLines = Lines.Where(l => affectedIds.Contains(l.JournalId));
        if (await allLines.AnyAsync(l => l.DebitAmount < 0m || l.CreditAmount < 0m ||
            (l.DebitAmount > 0m && l.CreditAmount > 0m) ||
            (l.DebitAmount == 0m && l.CreditAmount == 0m), ct))
            return ApiResponse<BankBookDto>.ErrorResponse("A bank journal contains an invalid posting line.", 409);
        if (await (from l in allLines
            group l by l.JournalId into g
            select new { Debit = g.Sum(x => x.DebitAmount), Credit = g.Sum(x => x.CreditAmount) })
            .AnyAsync(x => x.Debit != x.Credit, ct))
            return ApiResponse<BankBookDto>.ErrorResponse("An unbalanced bank journal prevents reporting.", 409);
        if (await allLines.AnyAsync(l => !db.Accounts.AsNoTracking().Any(a =>
            a.TenantId == user.TenantId && !a.IsDeleted && a.Id == l.AccountId), ct))
            return ApiResponse<BankBookDto>.ErrorResponse("Bank journal references a missing or deleted ledger account.", 409);

        if (bank.CampusId.HasValue && await (from l in Lines
            join j in affected on l.JournalId equals j.Id
            where l.AccountId == bank.AccountId &&
                (l.CampusId ?? j.CampusId) != bank.CampusId.Value
            select l.Id).AnyAsync(ct))
            return ApiResponse<BankBookDto>.ErrorResponse("Bank journal campus attribution does not match the bank account.", 409);

        var movements = from l in Lines
            join j in affected on l.JournalId equals j.Id
            where l.AccountId == bank.AccountId
            select new { l.Id, l.LineNo, l.DebitAmount, l.CreditAmount, l.Description,
                j.JournalDate, j.JournalNumber, j.SourceType, j.ReferenceNumber };
        var opening = await movements.Where(x => x.JournalDate < fromDate)
            .Select(x => (decimal?)(x.DebitAmount - x.CreditAmount)).SumAsync(ct) ?? 0m;
        var range = movements.Where(x => x.JournalDate >= fromDate && x.JournalDate <= toDate);
        if (await range.CountAsync(ct) > 5000)
            return ApiResponse<BankBookDto>.ErrorResponse("More than 5,000 bank movements; narrow the reporting period.", 422);
        var rows = await range.OrderBy(x => x.JournalDate).ThenBy(x => x.JournalNumber)
            .ThenBy(x => x.LineNo).ThenBy(x => x.Id).ToListAsync(ct);
        decimal running = opening, inflows = 0m, outflows = 0m;
        var entries = new List<BankBookLineDto>(rows.Count);
        foreach (var row in rows)
        {
            inflows += row.DebitAmount;
            outflows += row.CreditAmount;
            running += row.DebitAmount - row.CreditAmount;
            entries.Add(new BankBookLineDto
            {
                Date = row.JournalDate, JournalNumber = row.JournalNumber,
                SourceType = row.SourceType, ReferenceNumber = row.ReferenceNumber,
                Description = row.Description, DebitAmount = row.DebitAmount,
                CreditAmount = row.CreditAmount, RunningBalance = running
            });
        }
        if (opening + inflows - outflows != running)
            return ApiResponse<BankBookDto>.ErrorResponse("Bank book totals do not reconcile.", 409);
        return ApiResponse<BankBookDto>.SuccessResponse(new BankBookDto
        {
            BankAccountId = bank.Id, LedgerAccountId = account.Id,
            BankName = bank.BankName, LedgerAccountCode = account.Code,
            LedgerAccountName = account.Name, FromDate = fromDate, ToDate = toDate,
            OpeningBalance = opening, TotalInflows = inflows,
            TotalOutflows = outflows, ClosingBalance = running, Entries = entries
        });
    }
}
