# EduOS V2 LMS — development handoff (2026-10-30)

Ownership: **V2 LMS**. V1 Student/Academic/Finance/HR and the five active V3 tasks are not part of this change. The release gate in `AGENTS.md` and `RELEASE_V1_V2_V3.md` remains authoritative; a green development suite alone is **not** production/pilot acceptance.

## Delivered workflows

- Course manager: server-filtered instructor and eligible-student pickers; manager-only course enrollment using the existing retry-safe enrollment API. No fixed student or institute limits.
- Instructor: private, tenant-authorized submitted answer/file review; downloadable clean attachments; page-scoped CSV export with spreadsheet-formula escaping; marks and feedback.
- Student: private submitted-file retrieval, full instructor feedback, assignment rubric preview and criterion-wise awarded-mark breakdown.
- Rubric: 0–20 ordered criteria, each with its own positive two-decimal maximum; the sum must equal `Assignment.MaxMarks`. Teacher/manager can replace a rubric only before any submission. Completed enrollments lock revisions; existing submitted evidence is immutable. Grading validates the entire criterion breakdown and scalar total and writes scores in one serializable transaction.
- Security: HTTP role/module gating plus service-level tenant, enrolled-student or owning-instructor authorization; tenant-composite SQL foreign keys and filtered unique rubric order; optimistic rowversion; no private download caching; logout/switch aborts pending downloads and clears learner evidence.
- Performance: page-size-limited reviews and search; cached academic eligibility per enrollment within the page rather than per assignment.

## Schema and compatibility

Apply **`20261030165000_LmsAssignmentRubric`** before deploying the API/UI that uses it. Adds `AssignmentRubricCriteria` and `AssignmentRubricScores` only. No existing assignments, grades, course enrollments, or files are rewritten. Old submissions without a rubric continue to use their existing scalar marks. Back up and rehearse migrations on a production-shaped SQL Server database; avoid dropping rubric tables after real grading data exists.

## Acceptance gates (not waived by development completion)

1. Release build, full automated .NET/JS regression suite, and targeted LMS HTTP/service tests green against the same commit.
2. SQL Server migration apply/rollback rehearsal, FK/index verification, concurrent rubric-grading and enrollment requests, and realistic tenant-volume query plans.
3. Authorized pilot of teacher/student workflows including Bengali/English, keyboard/mobile usability, scanned uploads, download expiry, academic eligibility, archived courses, rubric grade evidence, backup/restore and operational monitoring.
4. Institute owner sign-off for online-only provider capability and approved live/online integrations. Do not call V2 LMS production-ready until these gates pass.

Only LMS code is included here; do not restart frozen V1 work or repurpose the V3 automated sprints for V2 without a new owner instruction.
