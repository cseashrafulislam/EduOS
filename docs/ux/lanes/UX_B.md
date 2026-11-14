# EduOS Unified UX — Lane B: Students, Admissions, Attendance & Library

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 42.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## academics:learners (20)

- [x] `/StudentSupport/Scholarships`
- [x] `/StudentSupport/Counseling`
- [x] `/Admissions/Forms`
- [x] `/Admissions/Offers`
- [x] `/Students/Index`
- [x] `/Students/Create`
- [x] `/StudentLifecycle/Index`
- [x] `/StudentDocuments/Index`
- [x] `/StudentGuardians/Index`
- [x] `/StudentTransferRequests/Index#destinationInbox`
- [x] `/EnrollmentManagement/Index`
- [x] `/LearnerIdentities/Index`
- [x] `/LearnerIdentities/AccessHistory`
- [x] `/Admissions/NewApplication`
- [x] `/Admissions/Applications`
- [x] `/Admissions/AssessmentSetup`
- [x] `/Admissions/Merit`
- [x] `/StudentImports/Index`
- [x] `/StudentReadmissions/Index`
- [x] `/StudentProgressions/Index`

## academics:portal (3)

- [x] `/StudentPortal/Index`
- [x] `/StudentSubjects/Index`
- [x] `/Alumni/Index`

## academics:operations (7)

- [x] `/AcademicRoutines/Index`
- [x] `/AcademicInstruction/Index`
- [x] `/AcademicCalendar/Index`
- [x] `/StudentLeaves/Index`
- [x] `/Attendance/Register`
- [x] `/Attendance/Reports`
- [x] `/Attendance/Corrections`

## management:library (12)

- [x] `/Library/Index`
- [x] `/Library/Study`
- [x] `/offline-study.html`
- [x] `/Library/StudyAssistant`
- [x] `/Library/Reservations`
- [x] `/Library/Branches`
- [x] `/Library/Overdue`
- [x] `/Library/Conditions`
- [x] `/Library/Integrity`
- [x] `/Library/Liabilities`
- [x] `/Library/Circulation`
- [x] `/Library/Digital`

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

None assigned yet. Update when selecting the first unchecked page.

## UX B source implementation completion — 2026-11-14

**UX implementation: 42 / 42 canonical sidebar routes marked UX Done.**
**Final QA: DEFERRED / NOT RUN for all routes as a single current-SHA acceptance run.** The UX status is based on source implementation, current branch audit and focused source contracts; it is **not** a full browser/SQL/security/production release sign-off.

**Source implementation and handoff:** [UX B completion report](UX_B_UX_COMPLETION_20261114.md).
The route inventory maps 42 destinations to 36 canonical Razor source views and one standalone offline-reader HTML document. Admissions and Attendance task routes intentionally share the same authorized canonical views and controller actions rather than duplicating UI/domain writes.

**Source changes pushed on `Ashraful`:**
- Server-managed internal Admission Intake, Book Category and Library Branch Codes with persistent, same-intent request IDs in browser and server validation; existing external explicit-Code API callers remain accepted. Initial change `c7b48c064a1ac6e0bdc021b657bc77e453a33ecf`, validation and replay hardening `522c6e92d5be77dc82e96a05f11a377ea8e1c1ae`, `033f7ea3a118c5c8ee069d90711543bc2aa14aeb`.
- Compact spacing, removal of visually duplicated page headers, and hiding of decorative Refresh controls without breaking historical JS bindings; learner/admission `707af397554178a60958ef2c779588ce755f8798`, learner/portal `42b2beab9c904063ddca9a608fc3578bf6dd4479`, operations/library `093fea65fd87dfc170722195e9a7b790088656ef`, remaining library `99aa529dfae200a4c86ddc98cf200c6d3f6cde93`.
- Eleven missing contextual bilingual Page Help guides added `01deb3a9c28bafec264f762e7dea4e4297cda21e`; the standalone offline reader received inline bilingual help `522c6e92d5be77dc82e96a05f11a377ea8e1c1ae`. Existing Help entries were retained.
- Six focused, fast UX B source-integrity contracts created `47ab09d193ef643740bcfe3046bc6e740eee5363` and repaired `0b6df2d997e1e6f4ae8413c32ec2b1c6c4b4b61d`; generated-Code conflict regression adapted to the non-editable Code model `6d8e28d6d9d74b57b23e377465084100b5f2a6da`.

**Source verification:** 36/36 source views have a shell title, no duplicate `h1`, balanced `thead` and existing linked local scripts; all 41 MVC routes have route-specific Page Help; offline reader has local Help; all four changed JS source files parse. The six new focused source checks **PASS** in [GitHub CI run 43573794856](https://github.com/cseashrafulislam/EduOS/actions/runs/43573794856), which overall **FAILS** due to other JavaScript tests; the repository-wide gate is not accepted. Follow-up legacy admission conflict test alignment is a new commit and its broader run must be checked independently. No test waiver and no database mutation.

**Separate Final QA backlog:** Current-head .NET Release build and all tests, SQL Server migration/backfill and persistence with a disposable database, authenticated Chromium scenarios per role/tenant/campus, UI screenshot acceptance on desktop/mobile and Bangla/English, shared AI/Voice field and read-only commands, CSRF/IDOR and status/concurrency/replay, applicant/guardian identity privacy, admission to enrollment and attendance consistency, Library borrow/renew/return/reservation, browser accessibility, upload security, report reconciliation, performance and rollback. The five scheduled UX automations remain OFF. No production sign-off until those gates pass.

## Screenshot-driven UX A + B compact layout correction — 2026-11-14

The shared app shell already renders the route title, so a second page header is **not allowed**. A previously hidden `h1`/`h2` can leave the old white header card and wasted height; the redundant **wrapper itself** was removed. One standalone help icon/back link no longer allocates a full toolbar row. Filter forms no longer combine Bootstrap `card`/`card-body` and `row` on the same element, which produced the screenshot's mostly blank left side and vertically stacked controls.

- Canonical re-audit: **UX A 41 routes / 28 views** and **UX B 42 routes / 36 MVC views plus the standalone offline reader**. Shared Academic/Institution/Admission/Attendance actions are counted once per unique source, not reimplemented.
- Focused corrections were pushed in `1d33873b7d3dcc360c1522b4ef83cc685545a2dc`, `c0049627a6bd0e776e2f437959eccb54ec926c46`, `0a86b2567709a6dfef13d5e05e9f244de783a3e8`, `78016f3edb76fd0933fe7ce0122f829733127c55`, `339bfbfcdf2814b325237236a70c55723bb03c52`, `0469c45f753b35e7f2bd51d7a39e6be11bdb54b8` and `7a778645717cfd2521ccb7e7e149ebd103571112`.
- The strict 83-route structural regression file is `EduOS.Tests/JavaScript/ux-ab-compact-layout.test.cjs` (`ed379886f7e413cc1b301a40eff5a683490ef01b`). It checks current route-to-view mapping, shared title ownership, removal of B-only header rows, safe Bootstrap grid nesting, balanced Library filter columns, conditional Merit content, preserved actions, and `aria-labelledby` targets.
- **No change to UX Done ledger totals. Final QA stays separate:** signed-in desktop/tablet/mobile, Bangla/English screenshot acceptance and latest-head full CI remain to be verified. The repository-level CI includes pre-existing JS failures; no broad PASS is claimed, no tests waived, and no database/tenant data modified. The scheduled UX automations stay OFF.

Full change narrative: [UX A/B screenshot correction](../UX_AB_COMPACT_LAYOUT_CORRECTION_20261114.md).
