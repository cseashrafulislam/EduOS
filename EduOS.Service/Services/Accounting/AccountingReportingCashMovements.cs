using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    // The designation is explicit. A generic asset or a bank-account name is
    // not sufficient evidence that a posting is physical cash-in-hand.
    public async Task<ApiResponse<LedgerStatementDto>> GetCashBookAsync(
        long accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<LedgerStatementDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (accountId <= 0 || !Dates(fromDate, toDate))
            return ApiResponse<LedgerStatementDto>.ErrorResponse("Valid cash ledger and reporting dates required.", 400);
        var cash = await db.Accounts.AsNoTracking().AnyAsync(a => a.TenantId == user.TenantId &&
            !a.IsDeleted && a.Id == accountId && a.IsCashInHand && a.IsActive &&
            !a.IsGroup && a.AllowsPosting && a.AccountType == AccountType.Asset && a.CurrencyCode == "BDT", ct);
        if (!cash)
            return ApiResponse<LedgerStatementDto>.ErrorResponse("Account is not a designated BDT cash-in-hand ledger.", 409);
        if (await db.BankAccounts.AsNoTracking().AnyAsync(b => b.TenantId == user.TenantId &&
            !b.IsDeleted && b.AccountId == accountId, ct))
            return ApiResponse<LedgerStatementDto>.ErrorResponse("Cash ledger conflicts with an existing bank mapping.", 409);
        return await GetLedgerStatementAsync(accountId, fromDate, toDate, ct);
    }

    // Direct cash movement control reconciles all explicitly designated cash
    // and uniquely mapped bank ledgers. It intentionally does not invent
    // operating/investing/financing allocations from account names/types.
    public async Task<ApiResponse<InstitutionalCashMovementDto>> GetInstitutionalCashMovementsAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Tenant administrator access required.", 403);
        if (!Dates(fromDate, toDate))
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Valid reporting dates required.", 400);
        var banks = await db.BankAccounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted).Select(x => new { x.Id, x.AccountId, x.CurrencyCode, x.CampusId }).ToListAsync(ct);
        if (banks.Count > 500 || banks.Any(x => x.CurrencyCode != "BDT" || x.AccountId <= 0) ||
            banks.GroupBy(x => x.AccountId).Any(g => g.Count() != 1))
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Bank-to-ledger mappings are not unique and BDT.", 409);
        var cash = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && x.IsCashInHand).ToListAsync(ct);
        if (cash.Count > 500)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("More than 500 cash ledgers; use a dedicated export.", 422);
        var bankIds = banks.Select(x => x.AccountId).ToHashSet();
        if (cash.Any(x => bankIds.Contains(x.Id)))
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Cash-in-hand ledger is also mapped to a bank.", 409);
        var ledgerIds = bankIds.Concat(cash.Select(x => x.Id)).Distinct().ToArray();
        if (ledgerIds.Length == 0)
            return ApiResponse<InstitutionalCashMovementDto>.SuccessResponse(new InstitutionalCashMovementDto {
                FromDate = fromDate, ToDate = toDate
            });
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && ledgerIds.Contains(x.Id)).ToListAsync(ct);
        if (accounts.Count != ledgerIds.Length || accounts.Any(x => x.AccountType != AccountType.Asset ||
            x.IsGroup || !x.AllowsPosting || !x.IsActive || x.CurrencyCode != "BDT"))
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Invalid cash or bank posting ledger.", 409);
        var rows = accounts.ToDictionary(x => x.Id, x => new InstitutionalCashMovementRowDto {
            LedgerAccountId = x.Id, LedgerCode = x.Code, LedgerName = x.Name,
            Kind = bankIds.Contains(x.Id) ? "Bank" : "Cash"
        });
        var historyError = await HistoricalPostingErrorAsync(null, toDate, ledgerIds, ct);
        if (historyError != null)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse(historyError, 409);
        var affected = Posted.Where(j => j.JournalDate <= toDate &&
            Lines.Any(l => l.JournalId == j.Id && ledgerIds.Contains(l.AccountId)));
        if (await affected.CountAsync(ct) > 10000)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("More than 10,000 cash journals; narrow the report or export.", 422);
        if (await affected.AnyAsync(j => j.CurrencyCode != "BDT", ct))
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("A cash journal has unsupported currency.", 409);
        var records = await (from line in Lines
            join journal in affected on line.JournalId equals journal.Id
            select new { JournalId = journal.Id, journal.JournalDate, JournalCampusId = journal.CampusId,
                line.AccountId, LineCampusId = line.CampusId, line.DebitAmount, line.CreditAmount })
            .Take(50001).ToListAsync(ct);
        if (records.Count > 50000)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("More than 50,000 cash journal lines; narrow the report.", 422);
        var usedIds = records.Select(x => x.AccountId).Distinct().ToArray();
        var validCount = await db.Accounts.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            !x.IsDeleted && usedIds.Contains(x.Id) && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT", ct);
        if (validCount != usedIds.Length)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Cash journal contains a missing/invalid posting account.", 409);
        var bankCampus = banks.ToDictionary(x => x.AccountId, x => x.CampusId);
        foreach (var journal in records.GroupBy(x => x.JournalId))
        {
            var lines = journal.ToArray();
            if (lines.Length < 2 || lines.Any(x => x.DebitAmount < 0m || x.CreditAmount < 0m ||
                (x.DebitAmount > 0m) == (x.CreditAmount > 0m)) ||
                lines.Sum(x => x.DebitAmount) != lines.Sum(x => x.CreditAmount))
                return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("A cash journal is invalid or unbalanced.", 409);
            var liquid = lines.Where(x => rows.ContainsKey(x.AccountId)).ToArray();
            var transfer = liquid.Length == lines.Length;
            if (transfer && (liquid.Select(x => x.AccountId).Distinct().Count() < 2 ||
                liquid.Sum(x => x.DebitAmount) != liquid.Sum(x => x.CreditAmount)))
                return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Ambiguous internal transfer in cash journal.", 409);
            if (!transfer && liquid.Any(x => x.DebitAmount > 0m) && liquid.Any(x => x.CreditAmount > 0m))
                return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Mixed transfer and external cash journal requires explicit allocation.", 409);
            foreach (var line in liquid)
            {
                if (line.JournalCampusId.HasValue && line.LineCampusId.HasValue &&
                    line.JournalCampusId != line.LineCampusId ||
                    bankCampus.TryGetValue(line.AccountId, out var campus) && campus.HasValue &&
                    (line.LineCampusId ?? line.JournalCampusId) != campus)
                    return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Cash movement campus does not match journal/bank scope.", 409);
                var row = rows[line.AccountId];
                if (line.JournalDate < fromDate) row.OpeningBalance += line.DebitAmount - line.CreditAmount;
                else if (transfer)
                {
                    row.TransfersIn += line.DebitAmount;
                    row.TransfersOut += line.CreditAmount;
                }
                else
                {
                    row.Receipts += line.DebitAmount;
                    row.Payments += line.CreditAmount;
                }
            }
        }
        var resultRows = rows.Values.OrderBy(x => x.Kind).ThenBy(x => x.LedgerCode).ToArray();
        foreach (var row in resultRows)
            row.ClosingBalance = row.OpeningBalance + row.Receipts - row.Payments +
                row.TransfersIn - row.TransfersOut;
        var report = new InstitutionalCashMovementDto {
            FromDate = fromDate, ToDate = toDate, Accounts = resultRows,
            OpeningBalance = resultRows.Sum(x => x.OpeningBalance),
            Receipts = resultRows.Sum(x => x.Receipts), Payments = resultRows.Sum(x => x.Payments),
            TransfersIn = resultRows.Sum(x => x.TransfersIn), TransfersOut = resultRows.Sum(x => x.TransfersOut),
            ClosingBalance = resultRows.Sum(x => x.ClosingBalance)
        };
        if (report.TransfersIn != report.TransfersOut ||
            report.OpeningBalance + report.Receipts - report.Payments != report.ClosingBalance)
            return ApiResponse<InstitutionalCashMovementDto>.ErrorResponse("Cash and bank movements do not reconcile.", 409);
        return ApiResponse<InstitutionalCashMovementDto>.SuccessResponse(report);
    }
}
