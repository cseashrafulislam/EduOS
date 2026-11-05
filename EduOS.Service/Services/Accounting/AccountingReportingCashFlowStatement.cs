using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Accounting;

public sealed partial class AccountingReportingService
{
    /// <summary>
    /// Direct method cash flow using approved, effective-dated nonliquid GL
    /// account rules. Unknown or compound mixed-direction entries stay
    /// explicitly unclassified, never guessed from account names or type.
    /// PolicyApprovedThroughUtc permits reproducible historical readbacks.
    /// </summary>
    public async Task<ApiResponse<InstitutionalCashFlowStatementDto>> GetInstitutionalCashFlowStatementAsync(
        DateOnly fromDate, DateOnly toDate, DateTime? policyApprovedThroughUtc = null,
        CancellationToken ct = default)
    {
        if (!TenantWideAllowed)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("Institution administrator access required.", 403);
        if (!Dates(fromDate, toDate))
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("Valid report dates required.", 400);
        var cutoff = policyApprovedThroughUtc ?? DateTime.UtcNow;
        if (cutoff.Kind != DateTimeKind.Utc || cutoff > DateTime.UtcNow.AddMinutes(1) || cutoff.Year < 2000)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("Policy approval cutoff must be a valid UTC instant.", 400);
        await using var snapshot = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var cash = await GetInstitutionalCashMovementsAsync(fromDate, toDate, ct);
        if (!cash.Success || cash.Data == null)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse(cash.Message, cash.StatusCode);
        var liquidIds = cash.Data.Accounts.Select(a => a.LedgerAccountId).ToHashSet();
        var categories = new[] { "Operating", "Investing", "Financing" }
            .ToDictionary(x => x, x => new InstitutionalCashFlowActivityDto { ActivityCode = x });
        var statement = new InstitutionalCashFlowStatementDto
        {
            FromDate = fromDate, ToDate = toDate,
            PolicyApprovedThroughUtc = cutoff,
            OpeningCashAndBank = cash.Data.OpeningBalance,
            ClosingCashAndBank = cash.Data.ClosingBalance
        };
        if (liquidIds.Count == 0)
        {
            statement.Activities = categories.Values.ToArray();
            if (snapshot != null) await snapshot.CommitAsync(ct);
            return ApiResponse<InstitutionalCashFlowStatementDto>.SuccessResponse(statement);
        }
        var journals = Posted.Where(j => j.JournalDate >= fromDate && j.JournalDate <= toDate &&
            Lines.Any(l => l.JournalId == j.Id && liquidIds.Contains(l.AccountId)));
        var rows = await (from l in Lines
            join j in journals on l.JournalId equals j.Id
            select new { j.Id, j.JournalDate, j.JournalNumber,
                l.AccountId, l.DebitAmount, l.CreditAmount })
            .Take(50001).ToListAsync(ct);
        if (rows.Count > 50000)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("Cash flow journal exceeds 50,000 lines; narrow the reporting period.", 422);
        var counterpartIds = rows.Select(x => x.AccountId).Where(id => !liquidIds.Contains(id)).Distinct().ToArray();
        var rules = await db.CashFlowActivityRules.AsNoTracking().Where(r => r.TenantId == user.TenantId &&
            !r.IsDeleted && r.StateCode == "Approved" && r.ReviewedAtUtc != null &&
            r.ReviewedAtUtc <= cutoff && r.EffectiveFromDate <= toDate &&
            counterpartIds.Contains(r.AccountId)).Take(10001).ToListAsync(ct);
        if (rules.Count > 10000)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("More than 10,000 classification rules; request a scoped policy audit.", 422);
        var byAccount = rules.GroupBy(x => x.AccountId).ToDictionary(x => x.Key,
            x => x.OrderByDescending(r => r.EffectiveFromDate).ThenByDescending(r => r.Id).ToArray());
        var exceptions = new List<CashFlowUnclassifiedDto>(50);
        foreach (var journal in rows.GroupBy(x => new { x.Id, x.JournalDate, x.JournalNumber }))
        {
            var lines = journal.ToArray();
            var cashLines = lines.Where(x => liquidIds.Contains(x.AccountId)).ToArray();
            if (lines.Length == cashLines.Length) continue; // Internal bank/cash transfer only.
            var cashIn = cashLines.Sum(x => x.DebitAmount);
            var cashOut = cashLines.Sum(x => x.CreditAmount);
            var counterpart = lines.Where(x => !liquidIds.Contains(x.AccountId)).ToArray();
            string? problem = null;
            if (cashIn > 0m && cashOut > 0m || cashIn == 0m && cashOut == 0m ||
                counterpart.Any(x => cashIn > 0m ? x.DebitAmount > 0m : x.CreditAmount > 0m) ||
                counterpart.Sum(x => cashIn > 0m ? x.CreditAmount : x.DebitAmount) !=
                    (cashIn > 0m ? cashIn : cashOut))
                problem = "Compound or mixed-direction cash allocation requires manual journal review.";
            var missing = false;
            if (problem == null)
            {
                foreach (var line in counterpart)
                {
                    var activity = byAccount.TryGetValue(line.AccountId, out var choices)
                        ? choices.FirstOrDefault(x => x.EffectiveFromDate <= journal.Key.JournalDate)?.ActivityCode
                        : null;
                    var value = cashIn > 0m ? line.CreditAmount : line.DebitAmount;
                    if (activity != null && categories.TryGetValue(activity, out var bucket))
                    {
                        if (cashIn > 0m) bucket.Inflows += value;
                        else bucket.Outflows += value;
                    }
                    else
                    {
                        missing = true;
                        if (cashIn > 0m) statement.UnclassifiedInflows += value;
                        else statement.UnclassifiedOutflows += value;
                    }
                }
            }
            else
            {
                statement.UnclassifiedInflows += cashIn;
                statement.UnclassifiedOutflows += cashOut;
            }
            if (problem != null || missing)
            {
                statement.UnclassifiedJournalCount++;
                if (exceptions.Count < 50)
                    exceptions.Add(new CashFlowUnclassifiedDto { JournalId = journal.Key.Id,
                        Date = journal.Key.JournalDate, JournalNumber = journal.Key.JournalNumber,
                        Reason = problem ?? "Approved classification missing for a counterparty on this journal date." });
            }
        }
        statement.Activities = categories.Values.ToArray();
        statement.Exceptions = exceptions;
        if (statement.Activities.Sum(x => x.Inflows) + statement.UnclassifiedInflows != cash.Data.Receipts ||
            statement.Activities.Sum(x => x.Outflows) + statement.UnclassifiedOutflows != cash.Data.Payments ||
            statement.Activities.Sum(x => x.Net) + statement.UnclassifiedInflows -
                statement.UnclassifiedOutflows != statement.NetCashChange)
            return ApiResponse<InstitutionalCashFlowStatementDto>.ErrorResponse("Classified cash flows do not reconcile to the cash/bank control ledger.", 409);
        if (snapshot != null) await snapshot.CommitAsync(ct);
        return ApiResponse<InstitutionalCashFlowStatementDto>.SuccessResponse(statement);
    }
}
