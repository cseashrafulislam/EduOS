# University OBE — published course-grade and assessment-component attainment (Issue #131)

## Source of truth and honest scope

`AccreditationProgramOutcomes` remains the authority for versioned programme learning outcomes (PLO). `AcademicCurriculum` / `CurriculumSubject` owns the course list. An immutable `ObePolicy` version defines one or more CLOs per curriculum course and weighted contributions to PLO versions. A CLO has **exactly one evidence source**: blank `ComponentCode` means the published whole-course-grade proxy; a configured `ComponentCode` means the corresponding immutable published assessment component. An OBE report reads **only** a canonical `ResultPublication` in `Published` state, its `StudentResultSummary` roster, immutable `StudentResultCourseSnapshot` rows, and (for component CLOs) the `ObePublishedComponentEvidence` register captured in the same publishing/correction transaction. It never writes assessment marks, results, fees, journals or student status.

**Important limitations:** course grades are still clearly labelled proxies and are *not* individual rubric scores. For a component CLO, the assessment component (e.g., a practical, quiz or rubric category) must have been present and reconciled **when the result was officially published**. If a component code was not captured, was missing from any learner, or the publication predates the additive capture migration, the report fails closed instead of borrowing editable marks. A component-code policy maps to a matching published assessment component; it does not reinterpret fine-grained rubric criteria that were never recorded as separate components in the canonical assessment system. No BAC/BAETE regulatory threshold/submission is inferred. An institution must approve its own effective-dated measurement policy and evidence selection.

## Effective-dated, reproducible formula

- The policy is append-only, per tenant/campus/program/version. The user selects specific immutable PLO versions, so later edits to programme structure do not change historical mappings.
- Each CLO is tied to a curriculum-subject version and optional published assessment-component code. Multiple CLOs can target one course only with **distinct** component codes (or one course-grade proxy). Each PLO's contribution weights must sum to **exactly 100.0000%**; duplicate `(PLO,CLO)` contributions are rejected.
- For a complete authorized published cohort, one eligible course snapshot must exist for **every** learner × mapped course, and one immutable component snapshot for **every** learner × mapped component CLO. The complete component register row counts must match the original published grade snapshot cohort. Absent, withheld, non-published, out-of-batch, duplicate or inconsistent records cause a hard failure. There is no missing-data substitution.
- For learner `s` and CLO `c`, `cloAttainment(s,c) = 100 × frozenObtainedMarks(s,c) / frozenFullMarks(s,c)`. Frozen marks come from the selected course or component evidence; no live assessment rows are read.
- For PLO `p`, `learnerPlo(s,p) = Σ cloAttainment(s,c) × configuredWeight(p,c)/100`, then `cohortPlo(p) = Σ learnerPlo(s,p)/numberOfLearners`. Only the final cohort score is rounded to two decimal places, away from zero. Each CLO displays its own cohort average.
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
- For a publication with at most 75,000 configured component evidence rows, the new capture uses one set-based SQL insert and an immutable completeness register in the canonical transaction. Over that cap, or if the additive schema is not yet installed during mixed-version deployment, academic result publishing remains valid and the component register is not created; rubric reporting fails closed, without silently degrading to a proxy.
- Deploy the additive migrations **before** enabling new OBE mapping/API. First back up and rehearse migration on restored data; existing publications are not backfilled from mutable marks. Rollback: disable the new route, preserve/export OBE policy/report/snapshot records. The component migration `Down` **cannot** be used while multiple CLOs are mapped to one course; data must first be archived with explicit data-loss approval. Never automatically run schema changes on production startup.
- SQL Server migration execution, `dotnet` Release build/full xUnit, tenant/campus/authenticated browser E2E, concurrent approval, restored-data migration/rollback and production p95 performance must be verified independently. JavaScript/contract checks alone are not final acceptance.
