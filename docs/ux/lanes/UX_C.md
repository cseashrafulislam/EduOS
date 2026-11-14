# EduOS Unified UX — Lane C: Learning, Exams, Research & Accreditation

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is the **page-by-page UX implementation ledger**. Existing checked routes may have exact-SHA Final QA evidence recorded below. Unchecked routes are not yet formally credited UX Done under the new process; a previously built page should be audited and reused, not reimplemented without a gap.

**Owner override — UX FIRST (2026-11-14):** Complete **all applicable UX implementation before the separate Final QA phase**. Work in canonical menu order; finish the first unfinished page's Razor/JS, backend/API where needed, backend-generated system Codes, compact responsive design, bn/en Help, shared AI/Voice and security-safe flows, then mark it **[x] UX Done** and immediately move on. Finish all pages of a menu before the next menu. Do not hold UX implementation hostage to expensive SQL/Chromium/browser Final QA. Do lightweight authoring review/syntax/compile checks where practical. Keep already verified routes intact, but recognize historic QA PASS is limited to the tested source SHA.

**Two status tracks:** checkbox **[x] = UX Done only**, not a claim that later Final QA has passed. Track `Final QA: Not Run / historical exact-SHA PASS / later PASS / FAIL` separately in the final-phase register. For every newly UX Done page, record route, code commit, changed files and deferred QA. No fake PASS, no test deletion and no waiver of tenant isolation, audit, financial/inventory or approval safety. Fix discovered critical defects during implementation. The 5 scheduled UX automations remain **OFF** per owner; this policy change does not enable them.

**Design standard:** One shared title, no top gaps, duplicate headings or decorative Refresh; practical mobile-friendly create/edit/list/report UI; multilingual Help, shared context-aware AI/Voice only where relevant; immutable server-generated internal Codes; reuse canonical domain data and backend validation. After all UX routes are done, run the full combined .NET/JS/SQL/Chromium/tenant/security/accessibility/locale/retry/report QA and repair any issues before release.

**Assigned canonical menu pages: 38.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## academics:learning (14)

- [x] `/Learning/Index`
- [x] `/Learning/StudyPlan`
- [x] `/TeacherStudio/Index`
- [x] `/TeacherStudio/Public`
- [x] `/TeacherStudio/AdaptiveGoals`
- [x] `/TeacherStudio/Cpd`
- [x] `/TeacherStudio/CpdReview`
- [x] `/Learning/Quizzes`
- [x] `/Learning/LiveClasses`
- [x] `/Learning/CourseContent`
- [x] `/Learning/AssignmentFiles`
- [x] `/Learning/Certificates`
- [x] `/Learning/CourseCompletions`
- [x] `/Learning/MySubmissions`

## academics:assessments (20)

- [x] `/AssessmentAdministration/ComponentMarks`
- [x] `/AssessmentAdministration/Omr`
- [x] `/AssessmentAdministration/SubjectCompletions`
- [x] `/AssessmentAdministration/AssessmentComponents`
- [x] `/AssessmentAdministration/AssessmentSchedules`
- [x] `/AssessmentAdministration/GradeSchemes`
- [x] `/AssessmentAdministration/CertificateTemplates`
- [x] `/AssessmentAdministration/Certificates`
- [x] `/AssessmentAdministration/Transcripts`
- [x] `/AssessmentAdministration/Marks`
- [x] `/AssessmentAdministration/MarkReviews`
- [x] `/AssessmentAdministration/Results`
- [x] `/AssessmentAdministration/Publications`
- [x] `/AssessmentAdministration/Setup`
- [x] `/AssessmentAdministration/Attempts`
- [x] `/AssessmentAdministration/ResultRescrutiny`
- [x] `/AssessmentAdministration/ResultFollowUp`
- [x] `/AssessmentAdministration/GradeDistribution`
- [x] `/AssessmentAdministration/ExamReports`
- [x] `/AssessmentAdministration/AdmitCards`

## academics:research (2)

- [x] `/ResearchProposals/Index`
- [x] `/ResearchGrants/Index`

## academics:accreditation (2)

- [x] `/Accreditation/Index`
- [x] `/Accreditation/Obe`

## Learning menu — source implementation checkpoint (2026-11-14)

**14/14 assigned Learning + Teacher Studio routes marked UX Done; 24 UX C routes remain.**
This is a source implementation checkpoint **only**; it is not browser, SQL, .NET release, production security or device-specific Final QA approval.

- Commits `524d7faf7b3e41840317766d3baba2346787b434` and `97fb9e14d39b58b884439865ac1cfeac42665067` correct all fourteen Learning/Teacher Studio view layouts: remove redundant top headers and help-only button rows, place essential actions in their actual course/search/table toolbars, fix nested Bootstrap card-body grids and avoid wasted Refresh columns. JS-bound certificate and completion help IDs are retained inside their relevant card headers.
- `75c94c1c0f71dfb24f3b2454d446851409b9684e` updates `EduOS.Core/DTOs/LMS/LmsDtos.cs`, `EduOS.Service/Services/LMS/LmsWorkflowService.cs`, `EduOS.App/wwwroot/js/learning/index.js`, `EduOS.App/Views/Learning/Index.cshtml`: create Course Code is server-generated using a stable client intent; legacy explicit-Code API contracts and existing edit version checks remain; per-tenant authority, author edit permission and exact-payload duplicate replay are enforced. `8d1aee7d78c612f65f6f3a0cdb211078dd41d62e` places Save/Clear together rather than a single button on a new row.
- `5574333317064ec66d01bbdc79aa47b169944ba2` adds 3 missing full bilingual Help guides (Study Plan, Course Completions, My Submissions) using the existing global Page Help renderer; all 14 now have route-specific bilingual Help.
- `0295d538d65680ca5b8daa76926d0240db08fc0e` adds eight `ux-c-learning-source-integrity.test.cjs` contracts for route/source ownership, single shell heading, balanced forms/cards, Page Help coverage, generated-code retries, preserved actions, aria labels and local JS assets. Source audit covered 14 distinct Razor views, found no top `<header>`, duplicate `content-header-card`, orphan `aria-labelledby`, mixed card/grid class or missing local JS. Full CI / .NET build / interactive browser checks remain independent and may still fail.
- Historical LMS/exam authorization and database behavior are preserved except the new generated-course create path described above. No data migration was needed and no production student/course records were changed. Five scheduled UX automations remain OFF.

### Learning route evidence (Final QA deferred)

| Route | UX source implementation commit | Modified scope | Final QA |
| --- | --- | --- | --- |
| `/Learning/Index` | `524d7faf` | `EduOS.App/Views/Learning/Index.cshtml` + shared Help, LMS DTO/service/JS | Deferred / not certified |
| `/Learning/StudyPlan` | `524d7faf` | `EduOS.App/Views/Learning/StudyPlan.cshtml` + shared Help | Deferred / not certified |
| `/TeacherStudio/Index` | `524d7faf` | `EduOS.App/Views/TeacherStudio/Index.cshtml` + shared Help | Deferred / not certified |
| `/TeacherStudio/Public` | `524d7faf` | `EduOS.App/Views/TeacherStudio/Public.cshtml` + shared Help | Deferred / not certified |
| `/TeacherStudio/AdaptiveGoals` | `524d7faf` | `EduOS.App/Views/TeacherStudio/AdaptiveGoals.cshtml` + shared Help | Deferred / not certified |
| `/TeacherStudio/Cpd` | `524d7faf` | `EduOS.App/Views/TeacherStudio/Cpd.cshtml` + shared Help | Deferred / not certified |
| `/TeacherStudio/CpdReview` | `524d7faf` | `EduOS.App/Views/TeacherStudio/CpdReview.cshtml` + shared Help | Deferred / not certified |
| `/Learning/Quizzes` | `97fb9e14` | `EduOS.App/Views/Learning/Quizzes.cshtml` + shared Help | Deferred / not certified |
| `/Learning/LiveClasses` | `97fb9e14` | `EduOS.App/Views/Learning/LiveClasses.cshtml` + shared Help | Deferred / not certified |
| `/Learning/CourseContent` | `97fb9e14` | `EduOS.App/Views/Learning/CourseContent.cshtml` + shared Help | Deferred / not certified |
| `/Learning/AssignmentFiles` | `97fb9e14` | `EduOS.App/Views/Learning/AssignmentFiles.cshtml` + shared Help | Deferred / not certified |
| `/Learning/Certificates` | `97fb9e14` | `EduOS.App/Views/Learning/Certificates.cshtml` + shared Help | Deferred / not certified |
| `/Learning/CourseCompletions` | `97fb9e14` | `EduOS.App/Views/Learning/CourseCompletions.cshtml` + shared Help | Deferred / not certified |
| `/Learning/MySubmissions` | `97fb9e14` | `EduOS.App/Views/Learning/MySubmissions.cshtml` + shared Help | Deferred / not certified |

**Next menu:** `academics:assessments` (20 routes), then Research (2), Accreditation (2).


## Examination menu — initial source implementation checkpoint (2026-11-14)

**3/20 assessment routes credited UX Done (Component Marks, Smart OMR, Subject Completions).** These are source/UI workflow implementation checks, **not Final QA** or certification of exam posting, scoring, images, roles, tenant isolation or database behavior.

- `aa27e7a787d89c36aecfcc38fce7d32888c3ec66` removes duplicated title/info-only headers; moves the JS-bound OMR manual button inside the selected assessment/component filter; repairs the Subject Completion filter as a real nested Bootstrap grid and preserves the year-end published-assessment eligibility note.
- `367dc693bd709ae80f39b28ee800a1ad4ebc3022` adds complete bilingual Page Help for all three routes without duplicating the global assistant.
- `f54b9b3ec0a95a5d5a957948f2735ca8f6a93bbc` adds five focused source-integrity contracts for title/header ownership, OMR manual button wiring, valid filter grids, bilingual help, and original role/operation controls. Current-head full CI and actual interactive OMR/posting testing remain separate.

| Route | UX source commit | Files | Final QA |
| --- | --- | --- | --- |
| `/AssessmentAdministration/ComponentMarks` | `aa27e7a` | `EduOS.App/Views/AssessmentAdministration/ComponentMarks.cshtml`, Page Help | Deferred |
| `/AssessmentAdministration/Omr` | `aa27e7a` | `EduOS.App/Views/AssessmentAdministration/Omr.cshtml`, Page Help | Deferred |
| `/AssessmentAdministration/SubjectCompletions` | `aa27e7a` | `EduOS.App/Views/AssessmentAdministration/SubjectCompletions.cshtml`, Page Help | Deferred |

**Historical checkpoint:** At this stage 17/38 UX C routes were complete. See the newer full source-implementation checkpoint below.

## UX C source implementation complete — 2026-11-14

**38/38 assigned routes marked UX Done (source implementation only): Learning 14; Examination 20; Research 2; Accreditation 2. Full Final QA is deferred and is NOT a PASS.** Five scheduled UX automations remain OFF.

- Exam layouts: commits `ad020340`, `5ddcceb2`, `35c76e33` compact the seventeen remaining Examination routes across fifteen physical Razor views (Marks/Results/Publications share `AssessmentAdministration/Index.cshtml`), preserve forms/actions and remove redundant headers. Commit `a9d1dc67` adds six missing bilingual Page Help guides. All 38 route keys have contextual Page Help.
- Server-owned internal create Codes: Assessment Component `e8504e3d`, Grade Scheme `e6999769`, Certificate Template `46cc3807`, Research Grant Scheme `e19f26fe`. Retry-stable request IDs and replay comparisons keep duplicate create requests from silently changing existing data; legacy explicit-Code API usage remains supported. Assessment Setup had already implemented server-owned assessment codes.
- Research Grants `f171a947`: owner-only, campus-scoped, server-paged ethics-approved proposal selector replaces manual GUID entry. The server independently checks the proposed grant and scheme. Research Proposals/Accreditation layout and role-visible OBE navigation: `d90bbae4`. Remove obsolete hidden Refresh controls and dead JS handlers: `f5dec021`.
- External semantic codes for accreditation standard, PLO and framework/version identity remain editable when needed for authoritative mapping and audit. They must not be replaced with arbitrary unrelated generated IDs.
- Source audit of 15 distinct Examination views plus Research/Accreditation views found no extra top title/header and balanced form tags. This is a lightweight source review, not browser, SQL, tenant, accessibility, mobile or final workflow certification.
- GitHub CI on commit `f171a947` completed the Core, Persistence and App **builds successfully**, but the overall JS contract suite **FAILED** (60 test failures across modules, including stale manual count assertions expecting 153 when 189 were registered). Current-head combined CI must be reviewed and failures fixed during the independent Final QA phase; no production release claim.

### UX C remaining-route evidence

| Area | UX route count newly completed | Primary source changes | Final QA |
| --- | ---: | --- | --- |
| Assessment administration | 17 | Layouts `ad020340`, `5ddcceb2`, `35c76e33`; generated-code DTO/service/JS `e8504e3d`, `e6999769`, `46cc3807`; Help `a9d1dc67` | Deferred |
| Research proposals + grants | 2 | Views/JS `d90bbae4`, `f5dec021`; grant scheme `e19f26fe`; eligible proposal API + UI `f171a947` | Deferred |
| Accreditation + OBE | 2 | Views/JS `35c76e33`, `d90bbae4`, `f5dec021` | Deferred |

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

All 38 assigned routes have source implementation checked. The separate combined Final QA has not passed and remains outstanding.
