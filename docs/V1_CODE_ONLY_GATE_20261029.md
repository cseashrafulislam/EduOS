# EduOS V1 — code-only release gate (2026-10-29)

This is a **development and automated-test checkpoint**, not a release approval or institutional UAT result. See `AGENTS.md` and `docs/RELEASE_V1_V2_V3.md` for the binding complete-module contract. The owner excludes institute-dependent real-user acceptance from the *code-only* progress measure; the release contract still requires it before production.

## Executed on the merged `Ashraful` working branch

| Gate | Evidence | Result |
| --- | --- | --- |
| Dependency restore | `.NET 10.0.403`, `dotnet restore EduOS.slnx` | Pass |
| C# Release build | `dotnet build EduOS.slnx --configuration Release --no-restore -m:1 /nr:false` | Pass, 0 errors (existing warnings remain) |
| C# tests | `dotnet test EduOS.slnx --configuration Release --no-build -m:1` | 2,635 passed, 0 failed, 0 skipped, re-run after latest V2 merge |
| Browser/JS contract tests | `node --test EduOS.Tests/JavaScript/*.test.cjs` | 263 passed, 0 failed, re-run after latest V2 merge |
| EF model/snapshot | `dotnet-ef migrations has-pending-model-changes --configuration Release --no-build` | No pending model changes |

These are local automated checks. A passing test suite does not imply SQL Server integration checks actually exercised a connected database; no SQL Server instance, restored-data rehearsal, representative peak-load evidence, or real-institution acceptance was available during this checkpoint. Existing test fixtures may use alternative in-memory implementations or early-exit without configured SQL Server.

## Implemented hardening in this checkpoint

- Discount assignments and fine applications now persist their non-empty `ClientRequestId` and reject changed-payload replay. Same-payload replay returns the original entity even if the master rule was subsequently deactivated.
- Tenant-leading filtered unique indexes enforce idempotency in SQL Server. Two new nullable columns preserve old records; no invented historical keys are backfilled.
- Finance UI keeps the same request ID across uncertain network retries for discount assignments, fine applications, refund requests and refund payout recording, and regenerates it when material inputs change.
- The existing `Ashraful` history was merged with concurrent V2 changes without deleting the V1 Academic commits. V2 work is not counted toward V1 completion.

## Open V1 code-owned blockers (not user-UAT items)

1. `FinanceAdjustmentRefunds.cs` intentionally rejects refunds against fully paid invoices. Implement and test a separately auditable, immutable **invoice credit-note and revenue reversal** flow; do not edit the paid invoice or misstate receivables. Update invoice reads, reconciliation, documents, and downstream statements consistently.
2. `FinanceAdjustmentService.cs` records fixed fines as administrative entries only. Integrate approved fines into invoice and GL ownership with an immutable source reference, safe waiver/correction and reports. Daily-fine billing is currently explicitly blocked. No financial effect may be represented until linked posting is implemented and tested.
3. Reconcile full V1 Fees/Accounts scope against actual API/UI and financial ledgers, including receivable/payable treatment, optional offered budgets, authorized corrections/reversals, and every promised report. Existing models and screens are not by themselves proof of delivered end-to-end workflow.
4. Final V1 cross-module scenarios must be run against SQL Server: Admission → Enrollment → Academic → Results → Fees; HR Payroll → Accounting; concurrency, tenant/campus ownership, printed/exported financial histories, migrations/backfills, and representative p95/p99 capacity.

**Release label:** V1 code-only gate **NOT COMPLETE**. Do not mark “ready for real-user UAT” merely because the automated suite is green. Unavailable production/UAT gates are tracked separately from these code-owned blockers.

## Migration safety

`20261029230000_StudentAdjustmentRequestIdempotency` is additive and allows legacy rows with NULL request keys. Deploy the schema before the updated write service; old code does not populate the new columns and remains compatible. Validate unique-index creation against a restored SQL Server copy before rollout. Down migration removes the keys and loses retry provenance: use an application rollback while retaining the additive schema whenever possible.
