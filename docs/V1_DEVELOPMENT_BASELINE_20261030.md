# EduOS V1 — verified development baseline and maintenance-only freeze

**Effective:** 2026-10-30. **Target:** `Ashraful`. **Decision:** stop repetitive V1 feature-development sprints; preserve the integrated baseline and its regression gates while V2 advances. This document is the current **engineering change-control status**, not a production-ready certificate or a substitute for the [module-complete release contract](RELEASE_V1_V2_V3.md).

## 1. Exact baseline and verification

| Evidence | Verified outcome |
| --- | --- |
| `Ashraful` integrated baseline | [`f19eb07ab39a96f4151fcf39ff082953b06cfac5`](https://github.com/cseashrafulislam/EduOS/commit/f19eb07ab39a96f4151fcf39ff082953b06cfac5) |
| GitHub CI, push on this SHA | [Run 41040885281](https://github.com/cseashrafulislam/EduOS/actions/runs/41040885281) — **success**: restore, build, JS/security/regression tests, EF snapshot, idempotent migration script, .NET tests, application publish |
| Disposable SQL Server migration smoke, same SHA | [Run 41040885184](https://github.com/cseashrafulislam/EduOS/actions/runs/41040885184) — **success**: fresh SQL Server 2022 migrations, repeat application, V1 admission/academic/finance/payroll SQL integration, independent V2/V3 smoke, isolated backup/restore and financial reconciliation drill |
| Recent V1 delivery | [#99](https://github.com/cseashrafulislam/EduOS/pull/99) finance credit-note GL integrity; [#100](https://github.com/cseashrafulislam/EduOS/pull/100) SQL recovery drill; [#101](https://github.com/cseashrafulislam/EduOS/pull/101) independent V1 SQL acceptance; [#102](https://github.com/cseashrafulislam/EduOS/pull/102) HR payroll loan recovery; [#104](https://github.com/cseashrafulislam/EduOS/pull/104) template-first dynamic Excel import; [#105](https://github.com/cseashrafulislam/EduOS/pull/105) branch/dependency consolidation — **merged** |

These gates verify the **implemented code and named automated scenarios**. They do not prove that every contractual V1 business workflow was exhaustively audited, that actual institute data has been accepted, that tax/PF treatment is legally signed off, or that provider/bank integrations, production load, restored production data and rollback are approved. **Do not equate CI green with full V1 production acceptance or assert “100% of every requirement”.**

## 2. V1 ownership and feature-development freeze

| V1-owned area | Treatment from this baseline |
| --- | --- |
| SaaS, institution, access, tenant/campus/platform security | Reuse current identity, entitlements, audit, privacy and configuration; no parallel security layer or alternate identity model |
| Admission, Students, Guardian, CSV/XLSX import | Reuse existing lifecycle and import job/receipt/reconciliation. The download template has fixed column headers; **institute count, student count, file count and rows are not product constants**. Validate the actual file and use bounded atomic batches, retries and configured plan quotas |
| Academic, Routine, Attendance, Examination, Results, Credentials | Reuse existing curriculum, registration, assessment, publication, correction/history, transcript/report contracts; never copy these into an invented V2 advanced-education module |
| Fees, Accounts, Finance | Preserve invoice, voucher, credit-note/reversal, fine and reconciliation audit history; no direct mutations of posted records or duplicate ledgers |
| HR & Payroll | Preserve employee/payroll/loan recovery/contra, statutory settings, payment, bank export and history; HR is **V1 only**, never an LMS/Library/Transport V2 deliverable |
| Shared UI/PWA, reports and role portals | Reuse bilingual tenant-safe components and permission-aware read models; no cosmetic rebuild merely to inflate completion |

**Feature-development freeze means:** Do **not** automatically create new V1 epics, regenerate controllers/services/views/tables, run blanket “what remains?” audits every V2 sprint, or revive unverified TODO lists from older branch-specific roadmaps. Existing code and migration history are the authoritative implementation. A docs-only declaration must never hide a missing critical permission or finance invariant discovered with evidence.

## 3. Exception policy — when V1 may be touched

Only make the **smallest necessary** reviewed V1 change for one of these reasons:

1. Reproducible correctness, security, tenant/campus/IDOR/privacy, financial balance, payroll or historical-data defect; failing CI/SQL regression, or a supported incident.
2. Approved statutory/tax/PF, banking/provider, deployment, restored-data, backup/restore or institutional acceptance correction.
3. An explicit new owner requirement with a documented V1 change request; do not quietly reinterpret V2 scope.
4. A narrowly justified V2 dependency or integration that cannot be implemented solely in LMS/Library/Hostel/Transport.

For each exception: link the issue/test evidence and affected V1 contract, inspect current `Ashraful` implementation, check tenant authorization and historical impact, avoid duplicate sources of truth, keep schema changes additive, add focused regressions, pass CI/SQL gates where relevant, and record rollback. Once closed, V1 returns to maintenance-only status. **Routine automated V1 regression execution is mandatory and does not reopen V1 feature development.**

## 4. Release acceptance still distinct from code freeze

- **Current engineering checkpoint:** integrated V1-related code is on `Ashraful`; the exact baseline passed the CI and disposable SQL Server suites above. No broad V1 rewrite is authorized by a prior status percentage.
- **Not established by these tests:** exhaustive functional checklist sign-off for every promised V1 scenario; institution-led UAT and statutory/bank/provider review; real-data migration and rollback rehearsal; representative multi-tenant peak-load, privacy/penetration, monitoring and operations approval; final owner go/no-go. These are tracked as **release acceptance/operations**, not V2 feature requests.
- **Historical records:** [V1 code-only gate of 2026-10-29](V1_CODE_ONLY_GATE_20261029.md), [Admission/Student handoff](ADMISSION_STUDENT_V1_CODE_GATE.md) and other dated documents are snapshots. Do not convert their old blockers into current work without checking later commits and tests.
- **Dynamic product rule:** no number such as a pilot institute/student count is a coded product limit. File-upload resource budgets (file size, validation and transactional batch size) and real configured subscription quotas remain valid operational protections.

## 5. Instructions for V2 agents, CI and future chats

1. Work on `Ashraful` (or a reviewed branch targeting it); **never edit `master`**. Read `AGENTS.md`, this baseline and the latest relevant code before choosing work.
2. V2 delivery is **Full LMS, Full Library, Full Hostel and Full Transport**. Integration & QA may validate V1 dependencies; it may not start another V1 HR/Finance/Academic sprint. V3 remains separate.
3. Do not re-label V1 “unfinished” from an old document, a missing commit ancestry after a squash merge, or the absence of real-user sign-off. Report separately: **verified code baseline**, **confirmed reproducible code defect**, and **pending production acceptance**.
4. Keep cross-release CI, SQL Server acceptance, backup/restore, tenant/security checks and regression tests enabled. A new failing commit or proven critical defect takes precedence over the feature freeze until corrected.
5. Do not suspend, retarget or edit existing approved automation schedules because of this document. The owner decides task ON/OFF. No application schema or runtime flags change merely by writing documentation.

**Status label for dashboards and summaries:** `V1 — engineering baseline verified, feature development frozen; maintenance and production acceptance open`. Never shorten this to `V1 production complete` without the missing sign-offs and scope evidence.
