# EduOS Unified UX — Lane B: Students, Admissions, Attendance & Library

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 42.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## academics:learners (20)

- [ ] `/StudentSupport/Scholarships`
- [ ] `/StudentSupport/Counseling`
- [ ] `/Admissions/Forms`
- [ ] `/Admissions/Offers`
- [ ] `/Students/Index`
- [ ] `/Students/Create`
- [ ] `/StudentLifecycle/Index`
- [ ] `/StudentDocuments/Index`
- [ ] `/StudentGuardians/Index`
- [ ] `/StudentTransferRequests/Index#destinationInbox`
- [ ] `/EnrollmentManagement/Index`
- [ ] `/LearnerIdentities/Index`
- [ ] `/LearnerIdentities/AccessHistory`
- [ ] `/Admissions/NewApplication`
- [ ] `/Admissions/Applications`
- [ ] `/Admissions/AssessmentSetup`
- [ ] `/Admissions/Merit`
- [ ] `/StudentImports/Index`
- [ ] `/StudentReadmissions/Index`
- [ ] `/StudentProgressions/Index`

## academics:portal (3)

- [ ] `/StudentPortal/Index`
- [ ] `/StudentSubjects/Index`
- [ ] `/Alumni/Index`

## academics:operations (7)

- [ ] `/AcademicRoutines/Index`
- [ ] `/AcademicInstruction/Index`
- [ ] `/AcademicCalendar/Index`
- [ ] `/StudentLeaves/Index`
- [ ] `/Attendance/Register`
- [ ] `/Attendance/Reports`
- [ ] `/Attendance/Corrections`

## management:library (12)

- [ ] `/Library/Index`
- [ ] `/Library/Study`
- [ ] `/offline-study.html`
- [ ] `/Library/StudyAssistant`
- [ ] `/Library/Reservations`
- [ ] `/Library/Branches`
- [ ] `/Library/Overdue`
- [ ] `/Library/Conditions`
- [ ] `/Library/Integrity`
- [ ] `/Library/Liabilities`
- [ ] `/Library/Circulation`
- [ ] `/Library/Digital`

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

None assigned yet. Update when selecting the first unchecked page.
