# EduOS — authoritative V1 / V2 / V3 module-complete release contract

**Owner decisions: 2026-10-29 (HR/Payroll moved from V2 to V1); 2026-10-30 (verified V1 development-baseline freeze and V2 LMS/Library engineering closure); 2026-10-31 (V2 Finance, Hostel/Transport end changes and exact-commit CI/SQL closure).** Supersedes the historic two-version contract at docs/RELEASE_V1_V2.md. Product targets: **one configurable education ERP** for schools/colleges, madrasa, university, training, coaching, online institute and private tutor. **Release scope is not proof of production approval.** For V1 see [V1 development baseline](V1_DEVELOPMENT_BASELINE_20261030.md); for V2's authoritative current status see [V2 Final Development Baseline](V2_FINAL_DEVELOPMENT_BASELINE_20261031.md); for closed LMS/Library see [their baseline](V2_LMS_LIBRARY_FINAL_BASELINE_20261030.md). The [earlier V2 QA checkpoints](V2_VERIFIED_ENGINEERING_QA_STATUS_20261031.md) are preserved as history, not current missing-feature evidence.

## Dynamic institution and learner scale; identity

No fixed number of institutions, students, Excel files or student rows is part of the product contract. School/college, madrasa, training, coaching, online, university and private-tutor presets are configurable, not a hardcoded list of institution slots. Excel import uses a downloadable template with **fixed field headers**, reads the real uploaded file contents, and processes validated, tenant-scoped, bounded, retryable batches; any protective resource limits must be documented operational controls, **not invented business caps**. Actual institution plan quotas, if configured, remain enforceable.

The same learner may have independent memberships in multiple institutes; one protected platform-global Person can link to independently tenant-owned Student/Enrollment data only after appropriate verification and consent. No cross-institute grades, guardian, fee or attendance disclosure. Canonical models remain institution-agnostic. Total records do not imply concurrent-user capacity.

## Non-negotiable ownership rule

A **named business module is completed entirely in one version**: setup, master data, transactions, approvals/statuses, exceptional cases, correction/reversal, security, multi-tenant/campus ownership, history, API, accessible bilingual/mobile UI, reports/print/export, load testing and user acceptance. Never plan “basic” in one release and “advanced” in the next. Ordinary maintenance/security fixes and compatible feature additions after launch are allowed; hidden known requirements cannot be deferred under an “advanced” label.

## Version 1 — Production-complete Education, Finance & HR/Payroll

- SaaS/institution foundation: signup/onboarding, institutional presets/configuration, multi-campus where applicable, identity, tenant membership, roles/permissions/ownership, MFA policy, entitlement/subscription, audits and safe account recovery.
- Admission and student lifecycle: intake/application, verification/assessment/merit/offer, guardian relationships, enrolment/registrations, transfer/promotion/repeat/readmission/completion, privacy-safe shared Person linking, authorized bulk CSV/XLSX import with fixed-header templates, preview, row errors, retries and reconciliation driven by actual uploaded records without a fixed cohort size.
- Full academic operations: programmes, levels/terms/year/curriculum versions, classes/cohorts/sections, subjects/courses/credits/prerequisites/electives as applicable, course registration, academic calendar, teacher assignments, timetable conflict control, class and learner attendance, approved correction and required reports.
- **Complete Examination/Assessment/Results:** assessment policy, exam/test/setup, written/MCQ/practical, marking & review/moderation, grade/GPA/CGPA/credit, failures/retake/reassessment/improvement, approval/publish/lock, authorized history-preserving correction, marksheet/tabulation/transcript/certificate/verification and all agreed reports.
- **Complete Fees and Accounts:** fee structures, instalments, scholarships/waivers/penalties, student billing/collections/due, payment verification and reconciliation, refund/reversal; complete chart of accounts, balanced voucher lifecycle, cash/bank, receivables/payables and offered budgets/period close, reports/statements/trial balance, audit/approval; no double posting.
- **Complete HR & Payroll:** recruitment and onboarding, employee identity/lifecycle/contracts and assignments, shifts, attendance, leave, effective-dated salary structures, allowances, bonuses, deductions, loans and recovery, configurable and lawfully reviewed statutory tax/PF settings, payroll periods/locks, maker-checker approval, payslips, bank account/payment review and export, approved payment/reconciliation, balanced finance posting/reversal, audit/history and reports. Preserve existing tenant/campus/role/plan isolation and signed historical payroll snapshots. **All remaining HR gaps, Bangladesh statutory sign-off, real bank-process acceptance, pilot UAT and production approval are V1 acceptance items, never V2 work.** Automated regressions alone do not certify production readiness.
- Required V1 role-specific admin/teacher/student/guardian/finance workflows, communication within promised channels, localization, reusable professional UI, per-user/institute theme/navigation preferences, page-header smart help, PWA and authorized reports/exports.
- **Do not call V1 complete** until all agreed V1 workflows pass authorization, integrity, concurrency, historical and real-institute UAT. Features absent from V1 must be securely gated and preserved.

**V1 development change control (effective 2026-10-30):** The verified `Ashraful` code baseline is feature-frozen pending real-world acceptance. Do not regenerate, redesign or repeatedly audit V1 modules on every V2 run. Existing V1 code and all security/integrity regression gates remain authoritative. Only documented reproducible defects, security/statutory work, formally approved owner changes and minimal V2 integration can reopen a V1 path under review. This freeze is **not** a declaration of general production readiness; see [the baseline and evidence](V1_DEVELOPMENT_BASELINE_20261030.md).

**Online LMS-provider caveat:** Three online-only institutes are in the launch cohort, but **full digital course/content/LMS capability is V2**. V1 may onboard them and handle only the V1 education/fee features they can actually use. Do **not** sell or declare their full LMS operation production-ready until V2 acceptance; do not pull partial LMS into V1 merely to claim all 16 can use every operation.

## Version 2 — Four complete new modules (no HR/Payroll)

| Module | Must be completed before enabled |
| --- | --- |
| Full LMS | Course/lesson/content and versioning, enrolment/access, quiz/question bank/attempts, assignment/submission/rubric/feedback, progress/grades, approved live/online integrations, completion/certificates, instructor/student flows, reports and content security |
| Full Library | Catalogue/copy tracking, issue/return/renewal/reservation, fine/waiver, lost/damaged, permissions, billing linkage and reports |
| Full Hostel | Buildings/rooms/beds, eligibility/capacity, reservations/allocation/transfer, occupancy/departure, fee and approved finance adjustments, incidents, security, reports |
| Full Transport | Drivers/vehicles/routes/stops, schedule/capacity, assigned student/reservation/transfer, transport fees and approved finance adjustments, maintenance, permissions, reports |

**V2 final development and defined automated QA, verified on `Ashraful` `ce0bb2e` (2026-10-31):**

| Module | Engineering work allocation | Release qualification |
| --- | --- | --- |
| LMS | **Development-complete; baseline frozen; maintenance-only** | Operational live-class/provider checks, pilot UAT, measured security/load/deployment evidence and sign-off remain external gates |
| Library | **Development-complete; baseline frozen; maintenance-only** | Real scanner/provider configuration, campus/Finance policy acceptance, restored-data rehearsal, browser UAT and sign-off remain external gates |
| Hostel | **Development-complete for the existing fixed-term reservation contract; maintenance-only** | Paid upcoming-start/end amendments use independent Finance approval, signed settlements, invoices or credit notes; the original 90-day booking limit remains. Comprehensive real-institute/paid-operation concurrency/browser/load acceptance is still required |
| Transport | **Development-complete for the agreed operations/reservation contract; maintenance-only** | Paid upcoming extension/shortening, signed Finance clearance, positive invoices and original-invoice credits implemented. Real-operator/guardian/browser/load/deployment and owner sign-off remain external gates |

**Aggregate V2 decision:** `Development Complete (agreed scope)` and `Defined CI + SQL Gates PASS` on [`ce0bb2e`](https://github.com/cseashrafulislam/EduOS/commit/ce0bb2e055f566616d81249e40af4b1d3a9b22e4); [CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500922) and [SQL smoke](https://github.com/cseashrafulislam/EduOS/actions/runs/41187500912) both **SUCCESS**. Required V2 Finance positive-invoice and negative paid/unpaid credit-note flows and Hostel/Transport cleared period changes are implemented. **Comprehensive institute UAT, representative load/browser/security and production go/no-go remain PENDING**; do not equate passing disposable CI with production authorization. Existing V2 implementations are now maintenance-only: no duplicate features, models, screens or a new “advanced” implementation in V3. See the [exact-commit V2 final baseline](V2_FINAL_DEVELOPMENT_BASELINE_20261031.md) for the scope and limits.

**V1 academic, results, finance and HR/Payroll must not be rewritten as “advanced V2”.** V2 LMS/Library/Hostel/Transport code, data and migrations remain preserved and development-frozen; live deployment stays gated on the separate operational acceptance described above. Existing HR/Payroll code and data remain intact as **V1**; do not re-create a V2 HR backlog or duplicate V1 payroll functions. Normal cross-module integration and security/bug/statutory maintenance are allowed without changing module ownership.

## Version 3 — Other complete enterprise modules

Complete Inventory/Procurement/Store; Asset and Facility lifecycle; Visitor/Campus operations; optional Career, Alumni, Research, Accreditation, advanced enterprise analytics and approved integrations—each scoped, delivered and accepted as a complete module. Reuse existing source-of-truth Finance, identity and stock flows; no duplicate financial or inventory facts.

**Inventory & Store Physical Stock Count V3 delivery (2026-10-31):** tenant/campus-scoped immutable book snapshots, blind and zero-quantity counts, maker submission/independent checker review, Automatic/Manual Final/Auto + Manual variance policies, atomic canonical receipt/issue posting, retry protection, linked audit/CSV reports and additive SQL migration are implemented on `Ashraful`. See [Physical Stock Count acceptance contract](operations/inventory-physical-stock-count.md). This is **code delivered pending exact-commit CI/SQL smoke and institutional UAT**, not a declaration that every V3 module or the production rollout is complete.

**Procurement → Inventory Goods Receipt V3 delivery (2026-10-31):** approved supplier PO, tenant/campus-scoped partial deliveries, immutable receipt/line evidence, maker submit and separate checker accept/reject, serialized cumulative PO quantity cap, replay/concurrency safety, Automatic/Manual Final/Auto + Manual unit-cost policy, atomically posted canonical Inventory receipt, linked CSV reporting and additive SQL migration are implemented as a discrete V3 integration slice. See [Goods Receipt acceptance and accounting boundary](operations/v3-procurement-goods-receipts.md). **Supplier invoices, PO/receipt/invoice three-way matching and Finance payable creation are not part of this slice** and remain pending. Exact-commit CI/SQL acceptance and institutional UAT are separate gates; the whole V3 is not complete.

**Inventory & Store final engineering closure (2026-10-31):** server-authorized printable Physical Count sheets with blind-draft redaction, bilingual mobile browser-flow regression, and disposable SQL three-store reconciliation/concurrent-transfer acceptance all passed at exact `Ashraful` commit [`522d6fa`](https://github.com/cseashrafulislam/EduOS/commit/522d6fafbcb6c4fffc7286b66d58167ef722daa2): [full CI](https://github.com/cseashrafulislam/EduOS/actions/runs/41197600092), [SQL acceptance](https://github.com/cseashrafulislam/EduOS/actions/runs/41197600094), [Chromium bilingual/mobile](https://github.com/cseashrafulislam/EduOS/actions/runs/41197600079) **SUCCESS**. Inventory engineering issues [#109](https://github.com/cseashrafulislam/EduOS/issues/109) and [#112](https://github.com/cseashrafulislam/EduOS/issues/112) are closed. The [engineering acceptance contract](operations/inventory-store-final-engineering-acceptance.md) still separates institutional p95/p99, restored/live-data UAT and deployment go/no-go as pending operational gates.

**Procurement Supplier Invoice Matching preflight (2026-10-31):** an Accountant/Principal/TenantAdmin-only, CSRF-protected bilingual read-only screen and API compare proposed invoice quantities/prices against approved PO and posted, non-reversed Goods Receipt → InventoryMovement evidence; manual or hybrid price differences are reasoned and displayed. This is a **proposal only**: invoice capture/uniqueness, cumulative overbilling protection, independent Finance posting, AP and payment are still pending. See [Supplier Invoice preflight acceptance](operations/v3-procurement-supplier-invoice-preflight.md). This new slice must pass its own exact-SHA CI/SQL gates before being claimed verified.

## Shared acceptance gate for every version/module

1. End-to-end workflows are real: persistence, invariant, role-scope, status transitions, rollback, corrections and reports; no stub UI/API or dead actions.
2. Cross-tenant, campus, guardian/learner ownership, direct-ID guessing, cache/file/export and multi-institute user switching tests pass.
3. Duplicate form submit, competing writes, import retries, sequential numbering, finance debit/credit balance, result lock and historical correctness tested.
4. Reasonable measured SQL plans, tenant-leading indexes, bounded paging/imports/exports; representative load with p95/p99 and resource/lock/error evidence. “Millions ready” is never declared by assumption.
5. Full Bangla/English, accessible responsive mobile/PWA, clean shared UI, contextual page help, persisted safe personalization, polished forms/dialogs/reports.
6. Additive migrations and backfills rehearsed on restored/anonymized data; no data loss, deployment/rollback/backup/restore and privacy-safe logging tested.
7. Exact commit/CI green where tests exist, security review and institute-user UAT, explicit owner go/no-go. Partial readiness is Under Construction, never labelled “100%”.

**Architecture:** maintain one existing SQL Server DB and EduOSDbContext and current modular monolith. No current database splitting or mass renaming. App deployment may be separated from SQL hosting when measured operationally appropriate. Any later multi-DB tenant relocation is a separately approved engineering project.

**Task execution:** V2 scope belongs only to LMS, Library, Hostel and Transport, and **all four are now closed to duplicate feature work**. The existing hourly automations are V3-focused under the owner override below. All work reads `AGENTS.md`, this contract, V1 freeze and the current V2 final baseline; checks current `Ashraful` HEAD; avoids duplicate/loop work; and leaves V1 and closed V2 modules in maintenance/acceptance. Tasks are not disabled unless the owner says OFF. A promised time target cannot waive tenant isolation, tests or data safety.

## Owner task allocation override — 2026-10-30

The owner explicitly retargeted the five **existing** hourly development tasks to V3,
superseding historical V2-task allocation sentences above and in AGENTS.md.
The five owned V3 tracks are Inventory & Store, Procurement, Fixed Assets,
Facilities & Campus, and Enterprise & QA. This changes task ownership, **not**
V1/V2 release boundaries or completion claims: preserve closed V2 LMS/Library
baselines and only evidenced V2 maintenance/operational acceptance, and do not duplicate V1 education,
finance, HR or V2 domain workflows in V3. Only the owner may turn tasks OFF.
