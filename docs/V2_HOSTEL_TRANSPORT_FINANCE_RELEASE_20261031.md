# EDUOS V2 Hostel + Transport — Finance release and QA handoff (2026-10-31)

## Scope and authoritative ownership

- Existing Hostel application, eligibility, waitlist, offer, guardian authority, bed allocation, resident, occupancy and reporting flows remain authoritative. Existing Transport fleet, route, stops, driver, student, trip, attendance, security and reporting flows remain authoritative. This release extends their operations; it does not replace them or create duplicate finance ledgers.
- `Ashraful` is the only deployment-candidate branch. Preserve frozen V1 and V2 LMS/Library implementations and concurrent V3 tasks. `master` is untouched by this handoff.
- The campus-specific Finance policies are **DailyProrated (auto)**, **FixedMonthly (auto)** or **Manual**; authorized users may override suggested amounts with a mandatory reason if campus policy allows. An estimate is never proof of a refund or payment.

## New audited Hostel and Transport operational flows

1. An authorized Finance administrator selects an active stay or rider, a valid operation, effective date and (for transfer) destination. A durable client request key prevents different replay payloads from sharing an approval.
2. The server validates tenant, campus, enrollment, route/bed/vehicle ownership, effective period and RowVersion; snapshots charge/deposit/fare terms and enrollment-scoped issued invoices, collections and refund evidence. It records a Pending Finance clearance without posting or editing accounting entries.
3. A different Finance administrator verifies the evidence and either rejects or approves. A nonzero credit/refund requires a **manually posted, independently approved, balanced** journal whose reference exactly matches `HOSTEL-CLEARANCE-{id}` or `TRANSPORT-CLEARANCE-{id}`, the correct campus, student and amount. Zero settlement needs an explicit no-adjustment attestation.
4. Hostel Operations / Transport Operations supplies the approved clearance ID. Inside a serializable transaction the service rechecks approval state, snapshot, source/target versions and current settlement journal. The approval is consumed once, before mutating the canonical reservation. Transfer creates a successor with a predecessor pointer; no historical stay, rider, fare, invoice or journal is rewritten.
5. Exact transport transfer retries return the same successor; source reuse, mismatched destination/date/clearance or duplicate successor fails. Hostel transfer uses the existing durable operation receipt. Both modules continue to permit strict zero-charge, no-posted-activity paths without paid clearances.

## Database change control

Apply migrations in order:

1. `20261030193510_HostelFinanceClearancePolicy` — adds Hostel billing policies and clearance ledger; campus/tenant composite FKs, one-use journal references, state constraints and indexes.
2. `20261030195303_TransportFinanceClearancePolicyAndLineage` — adds Transport policies and clearance ledger, and nullable `StudentTransports.PreviousAssignmentId` with tenant-safe FK/unique successor protection.

Both `Up` methods are additive. There is no rewriting of historical invoice, accounting, hostel or transport transaction rows. Back up the production SQL database and rehearse the migrations on a **restored copy** before rollout. Verify lock duration, table index build, application health, rollback feasibility, and post-deployment integrity reports. Rollback of these migrations after new clearances are consumed risks losing lineage/approval evidence and must be done through a reviewed restore/forward-fix plan—not casual `Down` execution.

## Invariants covered by the release

- Tenant and campus isolation on request, review, listing and execution; no cross-tenant foreign-key references.
- RowVersion and idempotency validation; unique client keys, unique source-successor lineage, one-use state and unique journal reference.
- Independent maker-checker, rejection, approved-only consumption; posted balanced journal and Finance exposure revalidation.
- Date-range and peak capacity validation for transfer; preserve initial assignment/stay, price and row ownership.
- Server authorization; anti-forgery headers on Finance UI writes; safe DOM text rendering and paged lists.

## Final QA and rollout decision

**2026-10-31 verified follow-up:** See [the Transport final QA handoff](V2_TRANSPORT_FINAL_QA_HANDOFF_20261031.md) and the [authoritative V2 development/QA checkpoint](V2_VERIFIED_ENGINEERING_QA_STATUS_20261031.md). The later `0f49e207` Transport-hardening commit passed the independent GitHub CI and disposable SQL Server finance/maker-checker/lineage gates; its descendant `23842bb7` also passed both exact-commit workflows. The evidence below belongs to the **earlier merged release** and remains a historical checkpoint, not the complete final QA matrix.

**Local merged-release evidence (2026-10-31, before GitHub CI):** Release solution build passed with 0 compilation errors (121 existing/remaining warnings); `.NET` xUnit **3,015 passed / 0 failed / 0 skipped**; Node.js contract suite **305 passed / 0 failed**; `dotnet-ef migrations has-pending-model-changes` reported no changes; `git diff --check` passed. Verification included the current fetched `Ashraful` V3 code, preserving the other automation-owned changes. These are local automated checks, not GitHub CI or production certification.

These are separate **operational/acceptance gates** and broader scenarios, not items to mark green solely because the fresh disposable CI SQL tests passed:

- Compile whole solution and execute the .NET xUnit and Node.js contract suites; run EF `has-pending-model-changes` and inspect generated migrations for destructive operations.
- On a real restored SQL Server copy, run all concurrency/race and financial posting/reversal integration tests, confirm that a second worker cannot consume the same clearance, and reconcile journals/invoices to the historical snapshots.
- Complete multi-tenant/campus/role/IDOR penetration and performance (pagination and peak occupancy) tests using representative large institute data.
- Complete Hostel and Transport staff/warden/guardian/mobile browser UAT, Finance maker/checker reconciliation, cash/refund bank handling (if applicable), backup/restore, rollback and deployment sign-off.

**Approval boundary:** code/build/unit/contract tests establish an engineering baseline only. Until restored-SQL rehearsal, real journal reconciliation and operational UAT sign-offs are collected, neither module nor the overall V2 release is certified as 100% production-ready. Do not reopen LMS/Library or V1 work solely to repeat closed development.

**Full-development boundary:** successful Finance-approved checkout/occupied transfer/closure does not yet cover charged Hostel pre-arrival reschedule or charged Transport upcoming-rider extension. Both operations are explicitly blocked pending a Finance billing/adjustment contract. Keep them in the V2 residual engineering scope; do not represent the entire module as 100% functionally complete, and do not create another Finance ledger.
