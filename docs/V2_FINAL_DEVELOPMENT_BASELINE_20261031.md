# EduOS V2 — final verified development baseline (2026-10-31)

**Decision: V2 DEVELOPMENT COMPLETE for the agreed implemented scope; defined automated CI/SQL release gates PASS. Production/UAT approval is NOT granted.**

**Repository:** `cseashrafulislam/EduOS` · **Branch:** `Ashraful` only · **Exact audited remote HEAD:** [`ce0bb2e055f566616d81249e40af4b1d3a9b22e4`](https://github.com/cseashrafulislam/EduOS/commit/ce0bb2e055f566616d81249e40af4b1d3a9b22e4). `master` was not changed as part of this V2 closure.

This document supersedes **development-status and missing-Finance-work claims** in earlier V2 QA, release, issue and handoff snapshots. Their historical test results and original scope decisions remain useful; dated claims that automatic reservation invoices, negative credit notes or Hostel/Transport paid-period changes are still unimplemented are no longer current. Evidence below certifies the **tested code baseline**, not every conceivable workflow or a live institution.

## Module and integration closure

| Owner | Development disposition at audited HEAD | Source of truth and important limits |
| --- | --- | --- |
| **LMS (V2)** | **Complete for owner-accepted scope; maintenance-only** | Existing enrolment, courses/lessons, revisions, assessments/submissions, progress, certificates, private-content/guardian integration, UI and tests. Reuse [frozen LMS/Library baseline](V2_LMS_LIBRARY_FINAL_BASELINE_20261030.md). Provider configuration and actual learners/instructors remain operational acceptance. |
| **Library (V2)** | **Complete for owner-accepted scope; maintenance-only** | Existing physical/digital catalogue, employee/student circulation, holds, overdue policy/notices, incident and liability/reversal flows, file privacy, reporting and tests. External malware scanner and real campus policy checks remain operational acceptance. |
| **Hostel (V2)** | **Development complete for implemented fixed-term reservation contract** | Applications/waitlist, beds/capacity, allocation/transfer/checkout and reports; Finance-cleared charged upcoming-start changes, **fixed-term end extensions within the existing 90-day reservation limit** and unstarted-stay shortening. Existing no-charge actions remain compatible aliases. Open-ended stays and renewal past that bound are **not** silently extended by this release. |
| **Transport (V2)** | **Development complete for implemented reservation/operations contract** | Route/vehicle/driver/stop, capacity, assignment and transfers, trip/attendance/readback, reports, Finance-cleared paid upcoming-rider extensions and shortening. Historical and idempotency contracts are preserved. |
| **Finance integration (shared V1 Finance, consumed by V2)** | **Required V2 reservation integration complete** | Campus-policy-aware signed quotes, independent maker/checker approval and single-use, date/version/campus/tenant-bound settlement; positive auto StudentInvoice/journal, zero attestation or independently posted manual journal, negative original-invoice **automatic credit note** with paid/unpaid reconciliation. No duplicate finance ledger and no automatic cash refund. |

**Policy:** Authorized institutes/campuses select Automatic, Manual Final or Auto + Manual Correction as supported by existing `DailyProrated` / `FixedMonthly` / `Manual` and `AllowManualOverride` configuration. Manual differences require a substantive reason, cent precision and independent approval. A preview is never an approval or a posted entry. See [dynamic institute policy](DYNAMIC_INSTITUTE_POLICY_RULE_20261031.md), [credit-note contract](V2_FINANCE_RESERVATION_CREDIT_NOTES_20261031.md) and [reservation end-change contract](V2_RESERVATION_END_AMENDMENTS_20261031.md). Student/institute counts are not fixed product constants.

## Same-commit verification — observed, not inferred

| Gate | Verified result | Evidence |
| --- | --- | --- |
| Canonical .NET Release build, EF snapshot/drift, idempotent migration SQL, application publish and CI | **PASS** | [GitHub CI 41187500922](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500922) on `ce0bb2e` |
| .NET full regression | **3,089 passed, 0 failed, 0 skipped** | Exact-commit CI `Test` log |
| Dedicated authentication security regression | **98 passed, 0 failed** | Exact-commit CI security step |
| JavaScript UI/workflow contracts | **316 passed, 0 failed** | Exact-commit CI and locally repeated `node --test EduOS.Tests/JavaScript/*.test.cjs` |
| Fresh and repeat SQL Server 2022 migrations | **PASS** | [SQL Server Migration Smoke 41187500912](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500912) |
| Disposable SQL V1 finance/identity, V2 transport/LMS and V3 coexistence; V2 Finance maker-checker/lineage and reservation-credit acceptance | **PASS** | Same SQL run, including the separate V2 Transport Finance gate |
| Disposable SQL backup/restore and financial evidence integrity | **PASS** | Same SQL run |

The SQL V2 Finance tests include paid-period extension posting, independent zero-adjustment clearance, date/nonce/replay/lineage guards, and shortening with an automatic **negative** original-invoice credit leaving historical invoice amounts unchanged. The separately tested credit contract covers paid/unpaid split. The code also contains the Hostel end-change path, shared Finance consumption and JavaScript/contract regressions. These gates do **not** constitute exhaustive Hostel paid-amendment SQL race coverage, a complete device/browser matrix or a measured load test; do not describe them as such.

The local audit compared `HEAD^{tree}` with freshly fetched `origin/Ashraful^{tree}`; both equal `951d1c451c387d0283298b24a337317297e98633`. Local Git commit IDs can differ from GitHub's commit SHA due to preserving non-destructive parallel history, but the audited **file contents** are identical. The local environment does not have the .NET SDK; its .NET/SQL evidence is the successful exact-SHA GitHub Actions runs, not a fabricated local execution.

## Remaining acceptance and operations — NOT a new V2 feature backlog

These tasks remain **not certified** and require the relevant institute, environment, provider or owner; passing code CI cannot waive them:

1. Restore an anonymized real-institute backup; rehearse forward migration, rollback/forward fix, invoice/credit/receipt reconciliation and recovery against representative historical data.
2. Run measured campus/multi-institute volume, representative workload and contention tests with p95/p99, query plans, SQL resource and error evidence. The optional synthetic 40K import rehearsal is **not** a V2 capacity guarantee or a fixed institute/student limit.
3. Complete institution-specific desktop/mobile/bilingual/accessibility and cross-role browser UAT, finance/warden/driver/librarian/instructor/guardian sign-offs; validate any operational live-class provider, device and Windows malware scanner configuration.
4. Obtain staged-deployment/security review, backup/restore, financial reconciliation, owner go/no-go and a rollback plan before live activation.

The current automated matrix is strong but non-exhaustive; report a **specific reproducible regression or uncovered safety-critical scenario** as maintenance/acceptance work, with tests, instead of reopening finished modules wholesale. Historic GitHub V2 issues may still be open because their external acceptance checklist is open or status text predates this code; reconcile each item against `ce0bb2e` and the linked exact-commit gate before treating it as missing functionality.

## Change-control and future work

- **V2 modules:** development baseline closed/frozen, including Finance-owned reservation integration; allow narrow reproducible bug, integrity/privacy/security, statutory, deployment-blocker or explicit owner-approved changes. Preserve existing entities, migrations, historical invoices/credits, APIs, UI and reports; no duplicate LMS/Library, Hostel/Transport or Finance architecture.
- **V1:** feature baseline remains independently frozen; its Finance records remain the only accounting source of truth. HR/Payroll, Admission/Student and Academic/Examination/Results are **not V2**.
- **V3:** independent release ownership; do not borrow completed V2 work to make a V3 feature. Five existing authorized V3 automation tracks are not changed by this documentation update.
- **Status labels:** `V2 Development = COMPLETE (agreed scope)`; `V2 Defined CI + SQL Gates = PASS (ce0bb2e)`; `V2 Comprehensive Production/UAT/Go-Live = PENDING`. **Never call this “100% production complete” without evidence and explicit sign-off.**
