# EduOS V1 — code-only release gate (2026-10-29)

> **Historical checkpoint only — superseded for current planning.** The blockers and test counts below describe the 2026-10-29 code state; they must **not** be copied into new V1 sprint backlogs without checking later merged implementations. The current 2026-10-30 `Ashraful` development baseline, passed CI/SQL evidence and mandatory feature-freeze/change-control rules are recorded in [V1_DEVELOPMENT_BASELINE_20261030.md](V1_DEVELOPMENT_BASELINE_20261030.md). Keep this file as immutable history, not the current status dashboard.

This is a **development and automated-test checkpoint**, not a release approval or institutional UAT result. See `AGENTS.md` and `docs/RELEASE_V1_V2_V3.md` for the binding complete-module contract. The owner excludes institute-dependent real-user acceptance from the *code-only* progress measure; the release contract still requires it before production.

## Executed on the merged `Ashraful` working branch

| Gate | Evidence | Result |
| --- | --- | --- |
| Dependency restore | `.NET 10.0.403`, `dotnet restore EduOS.slnx` | Pass |
| C# Release build | `dotnet build EduOS.slnx --configuration Release --no-restore -m:1 /nr:false` | Pass, 0 errors (existing warnings remain) |
| C# tests | `dotnet test EduOS.slnx --configuration Release --no-restore -m:1 /nr:false` | 2,654 passed, 0 failed, 0 skipped on merged V1/V2 code; SQL Server tests require separately configured connection |
| Browser/JS contract tests | `node --test EduOS.Tests/JavaScript/*.test.cjs` | 268 passed, 0 failed on merged V1/V2 code |
| EF model/snapshot | `dotnet-ef migrations has-pending-model-changes --configuration Release --no-build` | No pending model changes |

These are local automated checks. A passing test suite does not imply SQL Server integration checks actually exercised a connected database; no SQL Server instance, restored-data rehearsal, representative peak-load evidence, or real-institution acceptance was available during this checkpoint. Existing test fixtures may use alternative in-memory implementations or early-exit without configured SQL Server.

## Implemented hardening in this checkpoint

- Discount assignments and fine applications now persist their non-empty `ClientRequestId` and reject changed-payload replay. Same-payload replay returns the original entity even if the master rule was subsequently deactivated.
- Tenant-leading filtered unique indexes enforce idempotency in SQL Server. Two new nullable columns preserve old records; no invented historical keys are backfilled.
- Finance UI keeps the same request ID across uncertain network retries for discount assignments, fine applications, refund requests and refund payout recording, and regenerates it when material inputs change.
- The existing `Ashraful` history was merged with concurrent V2 changes without deleting the V1 Academic commits. V2 work is not counted toward V1 completion.

## Additional fixed-fine financial workflow (follow-up)

- An assessed fixed fine can be posted by a **different** tenant administrator as a dedicated `FIN-` invoice and balanced institutional journal in one serializable transaction; it never changes an existing tuition or paid invoice.
- `StudentInvoiceLine.StudentFineId` identifies the source fine; a tenant-scoped unique index (including deleted rows) and deterministic invoice/journal keys prevent duplicate billing. Fine-specific fee heads must have valid receivable/income accounts and must not be shared with standard fee-structure or historical tuition lines.
- The fine register separates assessed, waived, unposted and posted totals; posted lines are linked back to invoices and block an administrative waiver. Refund/correction of an already-posted fine remains a separately audited credit-note/reversal requirement.
- The additive migration `20261030010000_V1SeparateFineInvoicePosting` adds only nullable linkage and new indexes; legacy invoice rows keep their original values.
- Tests cover maker-checker, retry, journal balance, mapping/period validation, tenant checks, post-waiver restrictions and register/readback. A SQL Server integration test is provided behind `EDUOS_SQL_AUTH_TEST_CONNECTION`, but cannot be counted as run without a connected disposable SQL Server database.

## Open V1 code-owned blockers (not user-UAT items)

1. `FinanceAdjustmentRefunds.cs` intentionally rejects refunds against fully paid invoices. Implement and test a separately auditable, immutable **invoice credit-note and revenue reversal** flow; do not edit the paid invoice or misstate receivables. Update invoice reads, reconciliation, documents, and downstream statements consistently.
2. **Posted fine credit-note/waiver/reversal**, and daily-fine assessment/billing are still incomplete. The new dedicated fixed-fine journal flow does not silently adjust issued tuition invoices or permit arbitrary write-offs.
3. Reconcile full V1 Fees/Accounts scope against actual API/UI and financial ledgers, including payable treatment, optional offered budgets, authorized corrections/reversals, and every promised report. Existing models and screens are not by themselves proof of delivered end-to-end workflow.
4. Final V1 cross-module scenarios must be run against SQL Server: Admission → Enrollment → Academic → Results → Fees; HR Payroll → Accounting; concurrency, tenant/campus ownership, printed/exported financial histories, migrations/backfills, and representative p95/p99 capacity.

**Release label:** V1 code-only gate **NOT COMPLETE**. Do not mark “ready for real-user UAT” merely because the automated suite is green. Unavailable production/UAT gates are tracked separately from these code-owned blockers.

## Migration safety

`20261029230000_StudentAdjustmentRequestIdempotency` is additive and allows legacy rows with NULL request keys. Deploy the schema before the updated write service; old code does not populate the new columns and remains compatible. Validate unique-index creation against a restored SQL Server copy before rollout. Down migration removes the keys and loses retry provenance: use an application rollback while retaining the additive schema whenever possible.
