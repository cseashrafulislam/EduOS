# C1 — Adaptive Learning (issue #128)

## Operational contract

- **Assessment source:** existing published LMS quizzes, questions and graded QuizAttempt records. Study plans never alter grades, transcripts, certificates or official reports.
- **Mapping:** an academic quiz can optionally map to one published chapter and one normalized skill; difficulty is Foundation (1), Standard (2), Challenge (3). Existing quizzes remain valid without adaptive mapping.
- **Teacher overrides:** a goal is scoped to the assigned active academic course, enrolled student, published chapter and mapped skill. A tenant principal/admin may manage courses in that tenant. TargetPercent is 0–100 with a teacher note and rowversion concurrency protection.
- **Recommendations:** the most recent graded and submitted evidence per quiz contributes earned/possible marks. Latest graded quiz percentages are weighted by question marks across a skill; outstanding attempts, quiz access window and difficulty determine a suggested practice quiz. Priority is No evidence, Review (below 50%), Practice (below the teacher target), or Extend.
- **History:** learners save a point-in-time JSON study-plan snapshot. The unique tenant/request key makes replays idempotent. Future goals, scores, chapter names and algorithms do not overwrite historical snapshots.
- **Explanations:** quiz authors may add a 2,000-character rationale per question. The existing authorized attempt-review endpoint includes it only for a graded attempt after the quiz closes or the learner has exhausted their attempt limit, and only when no unfinished retry remains. Candidate/attempt-start endpoints never expose explanations or correct-option flags. Existing questions have no rationale by default.
- **Authorization:** every API requires an LMS-enabled tenant session, role and server-side course/enrollment ownership. Student endpoints never accept an arbitrary student ID; teacher mutations validate the instructor, course, chapter, student enrollment and mapped published quiz within the transaction.

## Pages and endpoints

| Actor | Page | API |
|---|---|---|
| Learner | /Learning/StudyPlan | GET /api/lms/adaptive/courses, GET /course/{guid}/plan, POST /plans, GET /course/{guid}/history, GET /history/{id} |
| Instructor/Principal/Admin | /TeacherStudio/AdaptiveGoals | GET /teacher/courses, GET /teacher/course/{guid}/roster, GET /teacher/course/{guid}/skills, GET /teacher/course/{guid}/goals/{studentGuid}, PUT /teacher/goals |
| Quiz author | /Learning/Quizzes | Existing quiz author API with optional chapter, skill and difficulty |

All mutating requests require antiforgery. UI uses textContent and tenant-session transition guards; API and database enforce authorization and integrity.

## Deployment and rollback

1. Back up production SQL and verify restore. Stage and apply migrations 20261109072000_AdaptiveStudyPlanning and 20261109083000_AdaptiveQuizExplanations in order. The second is deliberately separate and conditionally adds the optional column, preserving both environments with the prior migration already applied and any short-lived C1 deployment containing the column; old quiz clients continue to work.
2. Deploy updated API, DI, quiz author, teacher and learner pages. Existing quiz clients omit the new fields and continue as before.
3. Validate new/old quiz create-edit-read, assigned-teacher target saves, student snapshots and historical quiz grades; test with two tenants and users.
4. Inspect lock durations and index utilization on large data. Do not silently roll back populated goal, mapping or snapshot evidence; the down migration intentionally blocks data loss.

## Verification checklist

- Unit: scoring, latest graded selection, authorization, canonical marks unchanged, teacher ownership, mapped skills, replay idempotency, historical immutability and explanation anti-cheating gates.
- API: unauthorized and cross-tenant requests 403/404; invalid skill/chapter/percent 400/404; stale rowversion 409; duplicate client request returns the original snapshot.
- Data: new FKs include TenantId; unique goal and request indexes; decimal targets and official marks; migration/model snapshot without drift.
- UX: Bangla/English, responsive tables, keyboard form controls, error/retry handling, clear current-vs-saved plan.
- Release: full CI, SQL migration smoke tests and owner UAT separately. UAT is excluded from C1 engineering scope.
