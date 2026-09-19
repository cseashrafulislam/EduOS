# EduOS production-hardening roadmap status

This file tracks remaining work on `codex/phase-0-security-foundation`. An item is only marked complete when implementation exists on this branch and CI has validated the relevant baseline.

## Verified baseline

- Branch: `codex/phase-0-security-foundation` (never `master`).
- CI baseline before this update: commit `305195201742634e29fbc08d3799ed6700ba8575` passed GitHub Actions CI run `723`.
- Library and Transport authorization boundaries have targeted regression coverage.
- Student/guardian and employee self-service authorization boundaries have targeted regression coverage.
- Realtime notifications are authenticated and tenant-isolated, with regression coverage.
- Legacy hostel student identifiers are normalized to `long`, with contract coverage.

## Roadmap

| Area | Status | Exit criteria |
| --- | --- | --- |
| Library operational workflow | Implemented; hardening/tests present | Keep build/tests green and close concrete workflow defects found during final review. |
| Transport operational workflow | Implemented; hardening/tests present | Keep build/tests green and close concrete workflow defects found during final review. |
| Core education + self-service gaps | Substantially implemented | Audit remaining teacher/employee/student/guardian workflows and add only evidence-based fixes. |
| Cross-module integrity / long-ID normalization | In progress | No mapped operational FK/ID contract remains on a legacy incompatible width; migrations remain safe. |
| Security / tenant isolation / idempotency / concurrency | In progress | Mutations are authorized, tenant-scoped, replay-safe where required, and concurrency-sensitive writes are protected. |
| Automated tests / migration validation | In progress | Targeted regressions cover fixes; build, tests, and EF migration/snapshot validation are green. |
| Documentation cleanup | In progress | README/status accurately reflect implemented modules and operational requirements. |
| Production-readiness review | Pending | Final branch CI green; no known critical/high-severity correctness, isolation, migration, or authorization blocker remains. |

## Review discipline

Before changing a schema or relationship, inspect the mapped entity, EF configuration, current migration/snapshot, API/service consumers, and existing tests. Do not introduce a migration merely to normalize an orphan/unmapped legacy class. Prefer targeted regression tests for every security or integrity defect fixed.

The branch is **not yet declared production-ready**. The remaining integrity and hardening audit, migration validation, documentation cleanup, and final review must complete before this status can be changed to complete.
