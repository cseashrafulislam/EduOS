# EduOS — authoritative V1 / V2 / V3 module-complete release contract

**Owner decisions: 2026-10-29 (HR/Payroll moved from V2 to V1); 2026-10-30 (verified V1 development-baseline freeze).** Supersedes the historic two-version contract at docs/RELEASE_V1_V2.md. Product targets: **one configurable education ERP** for schools/colleges, madrasa, university, training, coaching, online institute and private tutor. **This is release scope, not evidence that implementation is complete or production-approved.** For current engineering status and what may reopen V1, see [V1 development baseline](V1_DEVELOPMENT_BASELINE_20261030.md).

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

**V1 academic, results, finance and HR/Payroll must not be rewritten as “advanced V2”.** Earlier LMS/Library/Hostel/Transport code, data and migrations remain preserved and gated while those V2 modules are completed. Existing HR/Payroll code and data remain intact as **V1**; do not re-create a V2 HR backlog or duplicate V1 payroll functions. Normal cross-module integration and security/bug/statutory maintenance are allowed without changing module ownership.

## Version 3 — Other complete enterprise modules

Complete Inventory/Procurement/Store; Asset and Facility lifecycle; Visitor/Campus operations; optional Career, Alumni, Research, Accreditation, advanced enterprise analytics and approved integrations—each scoped, delivered and accepted as a complete module. Reuse existing source-of-truth Finance, identity and stock flows; no duplicate financial or inventory facts.

## Shared acceptance gate for every version/module

1. End-to-end workflows are real: persistence, invariant, role-scope, status transitions, rollback, corrections and reports; no stub UI/API or dead actions.
2. Cross-tenant, campus, guardian/learner ownership, direct-ID guessing, cache/file/export and multi-institute user switching tests pass.
3. Duplicate form submit, competing writes, import retries, sequential numbering, finance debit/credit balance, result lock and historical correctness tested.
4. Reasonable measured SQL plans, tenant-leading indexes, bounded paging/imports/exports; representative load with p95/p99 and resource/lock/error evidence. “Millions ready” is never declared by assumption.
5. Full Bangla/English, accessible responsive mobile/PWA, clean shared UI, contextual page help, persisted safe personalization, polished forms/dialogs/reports.
6. Additive migrations and backfills rehearsed on restored/anonymized data; no data loss, deployment/rollback/backup/restore and privacy-safe logging tested.
7. Exact commit/CI green where tests exist, security review and institute-user UAT, explicit owner go/no-go. Partial readiness is Under Construction, never labelled “100%”.

**Architecture:** maintain one existing SQL Server DB and EduOSDbContext and current modular monolith. No current database splitting or mass renaming. App deployment may be separated from SQL hosting when measured operationally appropriate. Any later multi-DB tenant relocation is a separately approved engineering project.

**Task execution:** The currently approved V2 automations own **only LMS, Library, Hostel and Transport**. They must read `AGENTS.md`, this contract and the V1 freeze document, check current `Ashraful` HEAD, avoid duplicate/loop work, and leave V1 modules in maintenance/acceptance rather than generating a new V1 backlog. They must not disable themselves unless the owner says OFF. A promised time target cannot waive tenant isolation, tests or data safety.

## Owner task allocation override — 2026-10-30

The owner explicitly retargeted the five **existing** hourly development tasks to V3,
superseding historical V2-task allocation sentences above and in AGENTS.md.
The five owned V3 tracks are Inventory & Store, Procurement, Fixed Assets,
Facilities & Campus, and Enterprise & QA. This changes task ownership, **not**
V1/V2 release boundaries or completion claims: preserve V2 LMS/Library/Hostel/
Transport code and unfinished acceptance, and do not duplicate V1 education,
finance, HR or V2 domain workflows in V3. Only the owner may turn tasks OFF.
