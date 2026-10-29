# Payroll accrual reversal (V1)

The Finance > Payroll GL workspace provides an independently approved contra-journal workflow for an **unpaid** posted payroll accrual. It does not erase or reopen a posted payroll run, payroll snapshot, journal, or journal lines.

1. Tenant administrator views posted payroll (turn off **Unposted only**), selects an accrual, an open accounting period and reversal date, provides a reason, and submits a unique retry key.
2. A different tenant administrator reviews the exact inverse lines, provenance and period, then approves or rejects. A submitted or approved request can be rejected with an audit reason.
3. A different actor from the initiating administrator posts the approved contra journal. The original remains `Posted`; the contra is a separate `Posted` journal linked by `ReversalOfJournalId`.
4. Retries with the same key yield the same request; a second active or posted reversal is rejected. Reconciliation reports should show both original and contra. Journal-line totals of each entry must remain balanced.

## Restrictions

- Only canonical, balanced BDT payroll-run journals with undeleted history and no existing reversal may enter the workflow.
- Any pending or successful salary-payment request, posted payment journal, or linked loan-advance recovery blocks reversal. These require separately audited settlement/reconciliation; the system never implicitly refunds salary or restores a loan balance.
- A posted or active reversal prevents creating a new salary-payment request for that accrual. Accounting-period closure, tenant isolation, user roles and optimistic row versions are checked server-side.
- Reversal is not a replacement for a payroll adjustment or correcting a paid payroll. Paid accruals require a separately designed payment/loan correction workflow and manual accounting review.

## Operations and audit

Supported actions: `RequestPayrollReversal`, `ApprovePayrollReversal`, `RejectPayrollReversal`, and `PostPayrollReversal`; each is recorded in the tenant audit log. The original journal stays queryable for historic reports. Do not physically delete posted journals, mutate posted lines, or bypass an accounting-period lock.

Verification is covered by `PayrollJournalReversalWorkflowTests` plus the JS contract test; SQL Server CI and finance-report reconciliation remain release gates. A user acceptance test is a separate deployment milestone.
