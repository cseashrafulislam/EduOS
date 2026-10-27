using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // Reconciled BDT bank liquidity, NOT an operating/investing/financing cash-flow statement.
    // Only canonical BankAccount mappings identify bank ledgers; no guessed cash-in-hand accounts.
    // ExternalInflows/Outflows mean non-bank counterparties, not necessarily external to the institution.
    public async Task<ApiResponse<BankLiquidityDto>> GetBankLiquidityAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (!Dates(fromDate, toDate))
            return ApiResponse<BankLiquidityDto>.ErrorResponse("Valid reporting dates (up to ten years) required.", 400);

        var banks = await db.BankAccounts.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        if (banks.Count > 500)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("More than 500 bank accounts; use a dedicated export.", 422);
        if (banks.Any(x => x.CurrencyCode != "BDT" || x.AccountId <= 0) ||
            banks.GroupBy(x => x.AccountId).Any(g => g.Count() != 1))
            return ApiResponse<BankLiquidityDto>.ErrorResponse("Bank-to-ledger mappings must be unique and BDT.", 409);

        var bankLedgerIds = banks.Select(x => x.AccountId).ToArray();
        var bankAccounts = await db.Accounts.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && bankLedgerIds.Contains(x.Id))
            .ToListAsync(ct);
        if (bankAccounts.Count != banks.Count || bankAccounts.Any(x =>
            x.AccountType != AccountType.Asset || x.IsGroup || !x.AllowsPosting || x.CurrencyCode != "BDT"))
            return ApiResponse<BankLiquidityDto>.ErrorResponse("Bank mapping references an invalid BDT posting asset account.", 409);
        var ledgerById = bankAccounts.ToDictionary(x => x.Id);
        var bankByLedger = banks.ToDictionary(x => x.AccountId);
        var rows = banks.Select(b => new BankLiquidityRowDto
        {
            BankAccountId = b.Id, BankName = b.BankName,
            LedgerCode = ledgerById[b.AccountId].Code, LedgerName = ledgerById[b.AccountId].Name
        }).ToDictionary(x => bankByLedger.Single(b => b.Value.Id == x.BankAccountId).Key);

        var affected = Posted.Where(j => j.JournalDate <= toDate &&
            Lines.Any(l => l.JournalId == j.Id && bankLedgerIds.Contains(l.AccountId)));
        if (await affected.CountAsync(ct) > 10000)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("More than 10,000 bank journals; narrow the report or use an export.", 422);
        if (await affected.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<BankLiquidityDto>.ErrorResponse("A posted bank journal is not denominated in BDT.", 409);
        var records = await (from l in Lines
            join j in affected on l.JournalId equals j.Id
            select new { JournalId = j.Id, j.JournalDate, JournalCampusId = j.CampusId,
                l.AccountId, LineCampusId = l.CampusId, l.DebitAmount, l.CreditAmount })
            .Take(50001).ToListAsync(ct);
        if (records.Count > 50000)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("More than 50,000 bank-journal lines; narrow the report.", 422);

        var usedAccountIds = records.Select(x => x.AccountId).Distinct().ToArray();
        var validAccountIds = await db.Accounts.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && usedAccountIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(ct);
        if (validAccountIds.Count != usedAccountIds.Length)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("A posted bank journal references a missing account.", 409);

        foreach (var journal in records.GroupBy(x => x.JournalId))
        {
            var lines = journal.ToArray();
            if (lines.Any(x => x.DebitAmount < 0m || x.CreditAmount < 0m ||
                (x.DebitAmount > 0m && x.CreditAmount > 0m) ||
                (x.DebitAmount == 0m && x.CreditAmount == 0m)) ||
                lines.Sum(x => x.DebitAmount) != lines.Sum(x => x.CreditAmount))
                return ApiResponse<BankLiquidityDto>.ErrorResponse("An invalid or unbalanced bank journal prevents reporting.", 409);

            var mapped = lines.Where(x => bankByLedger.ContainsKey(x.AccountId)).ToArray();
            var hasOther = mapped.Length != lines.Length;
            var bankDebit = mapped.Sum(x => x.DebitAmount);
            var bankCredit = mapped.Sum(x => x.CreditAmount);
            if (bankDebit > 0m && bankCredit > 0m && hasOther)
                return ApiResponse<BankLiquidityDto>.ErrorResponse("A mixed bank-to-bank and non-bank journal requires explicit allocation.", 409);
            var transfer = !hasOther;
            if (transfer && (mapped.Select(x => x.AccountId).Distinct().Count() < 2 ||
                bankDebit != bankCredit))
                return ApiResponse<BankLiquidityDto>.ErrorResponse("A bank-only journal has ambiguous transfer attribution.", 409);

            foreach (var line in mapped)
            {
                var bank = bankByLedger[line.AccountId];
                if (line.JournalCampusId.HasValue && line.LineCampusId.HasValue &&
                    line.JournalCampusId != line.LineCampusId ||
                    bank.CampusId.HasValue && (line.LineCampusId ?? line.JournalCampusId) != bank.CampusId)
                    return ApiResponse<BankLiquidityDto>.ErrorResponse("Bank movement has inconsistent campus attribution.", 409);
                var row = rows[line.AccountId];
                if (line.JournalDate < fromDate)
                {
                    row.OpeningBalance += line.DebitAmount - line.CreditAmount;
                    continue;
                }
                if (transfer)
                {
                    row.TransferIn += line.DebitAmount;
                    row.TransferOut += line.CreditAmount;
                }
                else
                {
                    row.ExternalInflows += line.DebitAmount;
                    row.ExternalOutflows += line.CreditAmount;
                }
            }
        }
        var resultRows = rows.Values.OrderBy(x => x.BankName).ThenBy(x => x.BankAccountId).ToArray();
        foreach (var row in resultRows)
            row.ClosingBalance = row.OpeningBalance + row.ExternalInflows - row.ExternalOutflows +
                row.TransferIn - row.TransferOut;
        var result = new BankLiquidityDto
        {
            FromDate = fromDate, ToDate = toDate, Banks = resultRows,
            OpeningBalance = resultRows.Sum(x => x.OpeningBalance),
            ExternalInflows = resultRows.Sum(x => x.ExternalInflows),
            ExternalOutflows = resultRows.Sum(x => x.ExternalOutflows),
            TransferIn = resultRows.Sum(x => x.TransferIn),
            TransferOut = resultRows.Sum(x => x.TransferOut),
            ClosingBalance = resultRows.Sum(x => x.ClosingBalance)
        };
        if (result.TransferIn != result.TransferOut ||
            result.OpeningBalance + result.ExternalInflows - result.ExternalOutflows != result.ClosingBalance)
            return ApiResponse<BankLiquidityDto>.ErrorResponse("Consolidated bank movements do not reconcile.", 409);
        return ApiResponse<BankLiquidityDto>.SuccessResponse(result);
    }
}
