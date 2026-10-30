using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

public sealed partial class PayrollJournalPostingService
{
    private sealed record LoanPostingAllocation(LoanAdvance Loan, long PayrollEmployeeId, long EmployeeId, decimal Amount);

    private static Guid PayrollLoanKey(long tenant, Guid payrollReference, long loanId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:PayrollLoanRecovery:" + tenant + ":" + payrollReference.ToString("N") + ":" + loanId));
        return new Guid(bytes.AsSpan(0, 16));
    }

    // The payroll snapshot stores one aggregate LOAN_RECOVERY line per employee. Resolve
    // it back to the canonical loans, never guess a receivable or silently over-recover.
    private async Task<(string? Error, List<LoanPostingAllocation> Items)> PrepareLoanRecoveriesAsync(
        PayrollRun run, IReadOnlyList<PayrollEmployee> people, IReadOnlyList<PayrollLine> payrollLines,
        long componentId, long receivableAccountId, CancellationToken ct)
    {
        var result = new List<LoanPostingAllocation>();
        var tenant = user.TenantId;
        var snapshots = payrollLines.Where(x => x.SalaryComponentId == componentId).ToList();
        if (snapshots.Count == 0 || snapshots.All(x => x.Amount == 0m))
            return (null, result);
        if (!run.CampusId.HasValue || !run.CalculatedAt.HasValue)
            return ("Loan withholding requires a campus-scoped calculated payroll.", result);
        if (snapshots.Any(x => x.Amount <= 0m || x.Description != "Scheduled loan recovery") ||
            snapshots.Select(x => x.PayrollEmployeeId).Distinct().Count() != snapshots.Count)
            return ("Loan recovery snapshot is ambiguous; recalculate payroll.", result);
        var personById = people.ToDictionary(x => x.Id);
        if (snapshots.Any(x => !personById.ContainsKey(x.PayrollEmployeeId)))
            return ("Loan recovery references an unknown payroll employee.", result);
        var employeeIds = snapshots.Select(x => personById[x.PayrollEmployeeId].EmployeeId).Distinct().ToArray();
        var loans = await db.LoanAdvances.Where(x => x.TenantId == tenant && !x.IsDeleted &&
            employeeIds.Contains(x.EmployeeId) && !x.IsClosed && x.OutstandingAmount > 0m &&
            x.IssueDate <= run.PeriodEnd).OrderBy(x => x.Id).Take(501).ToListAsync(ct);
        if (loans.Count > 500)
            return ("Too many loans to reconcile safely.", result);
        var loanIds = loans.Select(x => x.Id).ToArray();
        var disbursements = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.SourceType == "LoanAdvance" && x.SourceId.HasValue &&
            loanIds.Contains(x.SourceId.Value)).ToListAsync(ct);
        var priorRecoveries = await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenant && loanIds.Contains(x.LoanAdvanceId))
            .Take(2001).ToListAsync(ct);
        if (priorRecoveries.Count > 2000)
            return ("Loan recovery history exceeds the safe reconciliation limit.", result);
        var (historyError, history) = await LoanRecoveryJournalHistory.ReadAsync(db, tenant, priorRecoveries, ct);
        if (historyError != null) return (historyError, result);
        foreach (var snapshot in snapshots)
        {
            var person = personById[snapshot.PayrollEmployeeId];
            var owned = loans.Where(x => x.EmployeeId == person.EmployeeId).ToArray();
            var scheduled = owned.Sum(x => Math.Min(x.InstallmentAmount, x.OutstandingAmount));
            if (owned.Length == 0 || scheduled != snapshot.Amount)
                return ("Payroll loan withholding no longer matches outstanding installments; recalculate payroll.", result);
            foreach (var loan in owned)
            {
                if (loan.CreatedAt > run.CalculatedAt.Value ||
                    (loan.UpdatedAt.HasValue && loan.UpdatedAt.Value > run.CalculatedAt.Value) ||
                    loan.PrincipalAmount <= 0m || loan.InstallmentAmount <= 0m ||
                    !Money(loan.PrincipalAmount) || !Money(loan.InstallmentAmount) ||
                    !Money(loan.OutstandingAmount))
                    return ("Loan was changed after payroll calculation or has invalid amounts.", result);
                var sources = disbursements.Where(x => x.SourceId == loan.Id).ToArray();
                if (sources.Length != 1 || sources[0].State != JournalState.Posted ||
                    sources[0].ReversalOfJournalId.HasValue ||
                    sources[0].CampusId != run.CampusId || sources[0].CurrencyCode != "BDT" ||
                    sources[0].JournalDate > run.PeriodEnd ||
                    (sources[0].PostedAt.HasValue && sources[0].PostedAt > run.CalculatedAt) ||
                    await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.ReversalOfJournalId == sources[0].Id && x.State == JournalState.Posted && !x.IsDeleted, ct))
                    return ("Loan disbursement is missing, reversed or outside payroll campus/date.", result);
                var sourceLines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.JournalId == sources[0].Id && !x.IsDeleted).ToListAsync(ct);
                if (sourceLines.Count != 2 ||
                    sourceLines.Count(x => x.AccountId == receivableAccountId &&
                        x.EmployeeId == loan.EmployeeId && x.DebitAmount == loan.PrincipalAmount &&
                        x.CreditAmount == 0m && x.CampusId == run.CampusId) != 1 ||
                    sourceLines.Count(x => x.CreditAmount == loan.PrincipalAmount &&
                        x.DebitAmount == 0m) != 1)
                    return ("Selected loan receivable does not match the original disbursement journal.", result);
                var previous = history.Where(x => x.Recovery.LoanAdvanceId == loan.Id).ToArray();
                if (loan.PrincipalAmount - LoanRecoveryJournalHistory.NetRecovered(previous) != loan.OutstandingAmount)
                    return ("Loan balance and recovery history do not reconcile.", result);
                foreach (var entry in previous)
                {
                    var recovery = entry.Recovery;
                    var priorJournal = entry.Source;
                    if (priorJournal.CampusId != run.CampusId)
                        return ("Earlier loan recovery journal needs reconciliation.", result);
                    var priorLines = await db.JournalLines.AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.JournalId == priorJournal.Id && !x.IsDeleted).ToListAsync(ct);
                    if (recovery.PayrollEmployeeId.HasValue)
                    {
                        if (priorJournal.SourceType != "PayrollRun" ||
                            priorLines.Count(x => x.Description == "Payroll loan recovery " + loan.Id &&
                                x.EmployeeId == loan.EmployeeId && x.AccountId == receivableAccountId &&
                                x.CreditAmount == recovery.Amount && x.DebitAmount == 0m) != 1)
                            return ("Prior payroll loan recovery cannot be reconciled.", result);
                    }
                    else if (priorJournal.SourceType != "LoanAdvanceRecovery" ||
                        priorJournal.SourceId != recovery.Id ||
                        priorLines.Count(x => x.AccountId == receivableAccountId &&
                            x.CreditAmount == recovery.Amount && x.DebitAmount == 0m) != 1)
                        return ("Prior cash loan recovery cannot be reconciled.", result);
                }
                var amount = Math.Min(loan.InstallmentAmount, loan.OutstandingAmount);
                var key = PayrollLoanKey(tenant, run.PublicId, loan.Id);
                if (await db.LoanAdvanceRecoveries.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == key && !x.IsDeleted, ct))
                    return ("An unlinked payroll loan recovery already exists.", result);
                result.Add(new LoanPostingAllocation(loan, person.Id, person.EmployeeId, amount));
            }
        }
        if (result.Sum(x => x.Amount) != snapshots.Sum(x => x.Amount))
            return ("Loan recovery allocations do not balance to payroll deductions.", result);
        return (null, result);
    }
}
