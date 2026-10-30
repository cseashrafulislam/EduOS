using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

public sealed partial class PayrollJournalPostingService
{
    private sealed record LoanRestoration(LoanAdvance Loan, decimal Amount);

    // Called inside the serializable reversal transaction. The original recovery rows stay
    // append-only; the posted payroll contra is their compensation evidence.
    private async Task<(string? Error, List<LoanRestoration> Items)> PlanLoanRestorationAsync(
        PayrollRun run, Journal original, IReadOnlyList<JournalLine> sourceLines, CancellationToken ct)
    {
        var result = new List<LoanRestoration>();
        var tenant = user.TenantId;
        var recoveries = await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.JournalId == original.Id)
            .Take(101).ToListAsync(ct);
        var postedLoanLines = sourceLines.Where(x => x.Description != null &&
            x.Description.StartsWith("Payroll loan recovery ", StringComparison.Ordinal)).ToArray();
        if (recoveries.Count > 100 || recoveries.Count != postedLoanLines.Length ||
            recoveries.Any(x => x.IsDeleted || !x.PayrollEmployeeId.HasValue || !x.JournalId.HasValue ||
                x.Amount <= 0m || decimal.Round(x.Amount, 2) != x.Amount || x.RecoveryDate != run.PeriodEnd) ||
            recoveries.Select(x => x.LoanAdvanceId).Distinct().Count() != recoveries.Count)
            return ("Original payroll loan allocation or recovery evidence is incomplete.", result);

        var snapshots = await (from line in db.PayrollLines.IgnoreQueryFilters().AsNoTracking()
            join employee in db.PayrollEmployees.IgnoreQueryFilters().AsNoTracking()
                on new { line.TenantId, Id = line.PayrollEmployeeId }
                equals new { employee.TenantId, employee.Id }
            join component in db.SalaryComponents.IgnoreQueryFilters().AsNoTracking()
                on new { line.TenantId, Id = line.SalaryComponentId }
                equals new { component.TenantId, component.Id }
            where line.TenantId == tenant && employee.PayrollRunId == run.Id &&
                component.Code == "LOAN_RECOVERY" && line.Amount != 0m
            select new { line.PayrollEmployeeId, employee.EmployeeId,
                line.Amount, line.Description, line.IsDeleted, EmployeeDeleted = employee.IsDeleted })
            .Take(501).ToListAsync(ct);
        if (snapshots.Count > 500 || snapshots.Any(x => x.IsDeleted || x.EmployeeDeleted ||
            x.Amount <= 0m || x.Description != "Scheduled loan recovery") ||
            snapshots.Select(x => x.PayrollEmployeeId).Distinct().Count() != snapshots.Count ||
            snapshots.Sum(x => x.Amount) != recoveries.Sum(x => x.Amount))
            return ("Original payroll loan withholding snapshots need reconciliation.", result);
        if (recoveries.Count == 0) return (null, result);

        var loanIds = recoveries.Select(x => x.LoanAdvanceId).ToArray();
        var loans = await db.LoanAdvances.Where(x => x.TenantId == tenant && loanIds.Contains(x.Id) &&
            !x.IsDeleted).ToListAsync(ct);
        if (loans.Count != loanIds.Length)
            return ("A recovered employee loan is missing or belongs to another tenant.", result);
        var history = await db.LoanAdvanceRecoveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && loanIds.Contains(x.LoanAdvanceId))
            .Take(2001).ToListAsync(ct);
        if (history.Count > 2000)
            return ("Loan history exceeds the safe reconciliation limit.", result);
        var (historyError, entries) = await LoanRecoveryJournalHistory.ReadAsync(db, tenant, history, ct);
        if (historyError != null) return (historyError, result);

        foreach (var recovery in recoveries)
        {
            var loan = loans.Single(x => x.Id == recovery.LoanAdvanceId);
            var employeeSnapshot = snapshots.SingleOrDefault(x => x.PayrollEmployeeId == recovery.PayrollEmployeeId);
            if (employeeSnapshot == null || employeeSnapshot.EmployeeId != loan.EmployeeId ||
                postedLoanLines.Count(x => x.Description == "Payroll loan recovery " + loan.Id &&
                    x.EmployeeId == loan.EmployeeId && x.CampusId == run.CampusId &&
                    x.CreditAmount == recovery.Amount && x.DebitAmount == 0m) != 1 ||
                loan.PrincipalAmount <= 0m || loan.OutstandingAmount < 0m ||
                loan.OutstandingAmount > loan.PrincipalAmount ||
                loan.IsClosed != (loan.OutstandingAmount == 0m))
                return ("Payroll loan, employee, balance or GL allocation is inconsistent.", result);
            var relevant = entries.Where(x => x.Recovery.LoanAdvanceId == loan.Id).ToArray();
            if (loan.PrincipalAmount - LoanRecoveryJournalHistory.NetRecovered(relevant) != loan.OutstandingAmount ||
                loan.OutstandingAmount + recovery.Amount > loan.PrincipalAmount)
                return ("Loan balance and immutable recovery ledger do not reconcile.", result);
            result.Add(new LoanRestoration(loan, recovery.Amount));
        }
        if (snapshots.Any(x => recoveries.Where(r => r.PayrollEmployeeId == x.PayrollEmployeeId)
                .Sum(r => r.Amount) != x.Amount))
            return ("Payroll loan installments do not match employee withholding snapshots.", result);
        return (null, result);
    }
}
