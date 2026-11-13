# EduOS Unified UX — Lane C: Learning, Exams, Research & Accreditation

Source: [canonical route ownership](../UX_ROUTE_OWNERSHIP_20261114.json). Branch: `Ashraful`. This is a **per-page UX + Final QA completion ledger**. Existing pages may already have partial implementation; unchecked means **NOT verified in this consolidated acceptance cycle**, not proof that the implementation is absent.

**Execution rule:** Select the FIRST unchecked route below, audit its related create/edit/detail/approval/report pages, complete all applicable UX/backend/AI-Voice/security improvements, execute required FINAL QA at the final commit SHA, and only then check the route. If tests fail or required QA is unavailable, leave unchecked and record `QA Pending / Blocked`; resume the same route next run rather than moving on. Record commit SHA, PASS/FAIL/BLOCKED/NOT RUN and affected shared regressions below. Update **only this lane's ledger**; other lanes' files are owned by their own tasks.

**Quality:** One shell title, no top gaps/duplicate notes, backend-generated immutable internal Codes, practical responsive forms/tables/reports, bn/en Help, appropriate shared Global Page Assistant (not per-page copies), server tenant/campus/role checks, concurrency/idempotency and source/report correctness. Run application build, targeted JS/.NET tests, applicable database/integration, actual mobile/browser checks; do not mark Verified Complete based on source-only assertions. Preserve existing APIs and history. Exact-code-SHA QA evidence is required.

**Assigned canonical menu pages: 38.** Related subpages are included in each route's verification, not counted again as separately assigned menu entries.

## academics:learning (14)

- [ ] `/Learning/Index`
- [ ] `/Learning/StudyPlan`
- [ ] `/TeacherStudio/Index`
- [ ] `/TeacherStudio/Public`
- [ ] `/TeacherStudio/AdaptiveGoals`
- [ ] `/TeacherStudio/Cpd`
- [ ] `/TeacherStudio/CpdReview`
- [ ] `/Learning/Quizzes`
- [ ] `/Learning/LiveClasses`
- [ ] `/Learning/CourseContent`
- [ ] `/Learning/AssignmentFiles`
- [ ] `/Learning/Certificates`
- [ ] `/Learning/CourseCompletions`
- [ ] `/Learning/MySubmissions`

## academics:assessments (20)

- [ ] `/AssessmentAdministration/ComponentMarks`
- [ ] `/AssessmentAdministration/Omr`
- [ ] `/AssessmentAdministration/SubjectCompletions`
- [ ] `/AssessmentAdministration/AssessmentComponents`
- [ ] `/AssessmentAdministration/AssessmentSchedules`
- [ ] `/AssessmentAdministration/GradeSchemes`
- [ ] `/AssessmentAdministration/CertificateTemplates`
- [ ] `/AssessmentAdministration/Certificates`
- [ ] `/AssessmentAdministration/Transcripts`
- [ ] `/AssessmentAdministration/Marks`
- [ ] `/AssessmentAdministration/MarkReviews`
- [ ] `/AssessmentAdministration/Results`
- [ ] `/AssessmentAdministration/Publications`
- [ ] `/AssessmentAdministration/Setup`
- [ ] `/AssessmentAdministration/Attempts`
- [ ] `/AssessmentAdministration/ResultRescrutiny`
- [ ] `/AssessmentAdministration/ResultFollowUp`
- [ ] `/AssessmentAdministration/GradeDistribution`
- [ ] `/AssessmentAdministration/ExamReports`
- [ ] `/AssessmentAdministration/AdmitCards`

## academics:research (2)

- [ ] `/ResearchProposals/Index`
- [ ] `/ResearchGrants/Index`

## academics:accreditation (2)

- [ ] `/Accreditation/Index`
- [ ] `/Accreditation/Obe`

## Verified completion evidence

| Route | Final commit SHA | Build / JS / .NET / Browser / SQL | Roles / Tenant / Device / Locale | Result / Notes |
| --- | --- | --- | --- | --- |

## Current in-progress or blocked route

None assigned yet. Update when selecting the first unchecked page.
