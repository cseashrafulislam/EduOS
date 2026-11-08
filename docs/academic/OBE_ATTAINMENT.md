# University OBE — published course-grade attainment (Issue #131, tranche 1)

## Source of truth and honest scope

`AccreditationProgramOutcomes` remains the authority for versioned programme learning outcomes (PLO). `AcademicCurriculum` / `CurriculumSubject` owns the course list. A new immutable `ObePolicy` version defines **one course-grade CLO proxy per curriculum course** and one or more weighted contributions to existing PLO versions. An OBE report reads **only** a canonical `ResultPublication` in `Published` state, its `StudentResultSummary` roster and the immutable `StudentResultCourseSnapshot` rows. It never writes assessment marks, results, fees, journals or student status.

**Important limitation:** a course grade is *not* individual rubric-component evidence. This tranche calculates a transparent course-grade proxy for CLO attainment. It does not claim component-level rubric attainment, multiple distinguishable CLOs per course, regulator-specific thresholds, or regulator submission. To satisfy the remaining component/rubric part of #131, the canonical result-publishing process must first freeze versioned **assessment-component evidence** (not read mutable `StudentAssessmentComponentMark`), then map reviewed CLO/rubric rows to those immutable components. That follow-up must include its own additive migration, version/reversal and reconciliation tests; the present course-grade proxy must never silently be relabeled as official rubric evidence.

## Effective-dated, reproducible formula

- The policy is append-only, per tenant/campus/program/version. The user selects specific immutable PLO versions, so later edits to programme structure do not change historical mappings.
- Each CLO proxy is tied to a curriculum-subject version. Each PLO's contribution weights must sum to **exactly 100.0000%**; duplicate `(PLO,CLO)` contributions are rejected.
- For a complete authorized published cohort, one eligible course snapshot must exist for **every** learner × mapped course. Absent, withheld, non-published, out-of-batch, duplicate or inconsistent records cause a hard failure. There is no missing-data substitution.
- For learner `s` and curriculum course `c`, `courseAttainment(s,c) = 100 × obtainedMarks(s,c) / fullMarks(s,c)`.
- For PLO `p`, `learnerPlo(s,p) = Σ courseAttainment(s,c) × configuredWeight(p,c)/100`, then `cohortPlo(p) = Σ learnerPlo(s,p)/numberOfLearners`. Only the final cohort score is rounded to two decimal places, away from zero. Each CLO proxy displays the same cohort average for its single curriculum course.
- All arithmetic uses `decimal`; no policy threshold is hard-coded. This report measures a percentage; any future pass/fail threshold must be effective-dated separately.

**Reconciliation example** (two fully evidenced learners; each course has 100 full marks):

| Learner | CLO1/course A | CLO2/course B | PLO1, 60% A + 40% B |
| --- | ---: | ---: | ---: |
| Student 1 | 80 | 50 | 68 |
| Student 2 | 60 | 90 | 72 |
| Cohort | 70% | 70% | **70%** |

The stored report freezes publication ID/version, mapping policy ID, aggregate CLO and PLO percentages, roster size and SHA-256 evidence digest. The **submitted snapshot cannot be edited**, including after a reviewer decision. An independent, current accreditation coordinator may approve or reject once with a reason. Approval recomputes the current published evidence digest and fails closed if the source was withdrawn/altered. Approved CSV omits student names, marks, private files and reviewer narratives.

## Transaction, privacy, scale and deployment

- Authorization uses current membership/role and explicit tenant/campus grant on all read/write/export paths. `RequireModule("ACCREDITATION")`, session-reference verification, antiforgery on writes and `no-store` apply to the API.
- Policy creation and report submission are serialized, unique-request-key idempotent operations. Reused keys with different payloads conflict; different request keys cannot duplicate the same version or report. Review is a compare-and-set single transition with independent reviewer identity. The SQL migration adds composite tenant FKs, unique keys, check constraints and freeze triggers. No existing row or historical migration is changed.
- Interactive calculations currently cap at 10,000 roster rows / 50,000 mapped evidence rows per publication. Exceeding this returns a visible error rather than an unbounded in-memory scan or timeout. A larger cohort needs an independently measured and authorized SQL aggregate/reconciliation report or asynchronous export, not a removed cap.
- Deploy the additive migration **before** enabling the new UI/API. First back up and rehearse migration on restored data; no backfill is required. Rollback: disable the new route, preserve/export new OBE policy/report records, then only drop the new tables when explicit data-loss approval exists. Never automatically run schema changes on production startup.
- SQL Server migration execution, `dotnet` Release build/full xUnit, tenant/campus/authenticated browser E2E, concurrent approval, restored-data migration/rollback and production p95 performance must be verified independently. JavaScript/contract checks alone are not final acceptance.
