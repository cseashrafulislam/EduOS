# Teacher CPD — canonical LMS integration (2026-11-09)

Owner: Education content / LMS (Task B3). Works on the unified `Ashraful` branch.

## Authoritative records

- `Courses.IsTeacherCpd` selects the audience. All pre-existing courses remain non-CPD.
- `CourseEnrollments` represents **either** one Student **or** one Employee per row; SQL enforces exactly one and employee-only rows cannot reference `StudentEnrollmentId`. There is no fabricated Student record.
- `LessonProgresses`, `Quizzes`, `QuizAttempts`, `QuizAnswers`, completion-state checks, `LmsCompletionCertificates` and audit stay on the **existing** canonical LMS records.
- CPD uses graded LMS quizzes for assessment. Student assignment authoring is refused for CPD so teachers cannot enroll in unfinishable assignment-dependent courses. Academic courses keep their previous assignment workflow.
- Historical certificate `StudentNameSnapshot`/`StudentCodeSnapshot` column names are intentionally preserved for API/database compatibility; employee CPD certificates use the same immutable holder name/code snapshots, additionally keyed to `EmployeeId`. A future breaking API rename is not part of this change.

## Workflow and access

1. Instructor or administrator creates an active CPD course in Learning, publishes lesson(s) and graded quiz(es).
2. Active tenant teacher enrolls **self** from `/TeacherStudio/Cpd`; request UUID and `(tenant, course, employee)` uniqueness prevent duplicate enrollment.
3. Teacher completes lessons and existing LMS quizzes. The quiz endpoints revalidate owner/tenant and only permit teacher participation in CPD courses.
4. Teacher finalizes enrollment through existing server-side completion evidence (published lessons, graded quizzes, passing marks and academic-scope validation). A rowversion and serializable transaction guard concurrent completion.
5. Administrator reviews employee completion through `/TeacherStudio/CpdReview`, then issues the canonical LMS certificate through the existing issuance service. Issuance is unique per enrollment/revision and audited. CPD employee certificates cannot be issued by an unrelated instructor; they require manager permission.
6. A revoked certificate is retained; authorized reissue creates a new revision with immutable historical evidence. Current printable certificate remains access-controlled for its employee recipient or course administrator.

## Migration and rollback

`20261109050000_TeacherCpdCanonicalEnrollment` is additive for existing data: a false audience bit, nullable employee identifiers, tenant FKs, filtered uniqueness and exactly-one-participant checks. Existing student rows remain unchanged. The student-only filtered index explicitly excludes employee rows. Review index locking and run on a restored production-sized copy first.

The migration `Down` refuses to delete CPD employee participation, certificates, or a CPD-marked course; preserve retention/backup requirements before any reverse migration. Do not delete posted certificate evidence to make rollback appear successful.

## Verification and operator boundaries

- Unit tests: teacher-only self-enrollment and idempotent replay; student/cross-employee access denial; cross-course lesson denial; monotonic completion progress.
- Release gates: exact-commit .NET CI, migration SQL smoke, concurrency and tenant tests. Browser pilot/UAT, restored production data, backup/restore and operational sign-off are separate and **not** implied by green automated tests.
- Public verification links remain opt-in and student-only; teacher CPD certificates are available to the authenticated employee/institution admin. An external CPD verification portal would need an explicit privacy/consent contract.
- Course authoring is locked against audience switches once enrollments exist and against published student assignments on CPD.
