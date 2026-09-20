# EduOS production-hardening roadmap status

This file tracks remaining work on `codex/phase-0-security-foundation`. An item is only marked complete when implementation exists on this branch and CI has validated the relevant baseline.

## Verified baseline

- Branch: `codex/phase-0-security-foundation` (never `master`).
- CI baseline: commit `85b1d7a883ac276578a9d8134f751572d0027897` passed GitHub Actions CI #779 on 2026-09-20.
- CI now validates a Release production publish of `EduOS.App` in addition to build, EF pending-model-change validation, and the automated test suite.
- Library and Transport authorization boundaries have targeted regression coverage, including authenticated read surfaces and privileged mutation boundaries.
- Library stock and issue lifecycle concurrency tokens are mapped and guarded by persistence contract tests.
- Transport assignment capacity/duplicate checks run inside a serializable transaction; database/transaction conflicts are mapped to HTTP 409, with targeted concurrency contract coverage.
- Library/Transport tenant-owned operational entities are guarded by model/query-filter isolation regression coverage.
- `BookIssue.ClientRequestId` and `StudentTransport.ClientRequestId` remain mapped retry/idempotency correlation keys. Tests intentionally guard the persisted contract without inventing a database uniqueness constraint that is absent from the current schema.
- Student/guardian and employee self-service authorization boundaries have targeted regression coverage.
- Realtime notifications are authenticated and tenant-isolated, with regression coverage.
- Legacy hostel student identifiers are normalized to `long`, with contract coverage.
- Legacy LMS `Quiz`/`QuizResult` identifiers are normalized to `long`, with contract coverage. These classes are not currently exposed as mapped `DbSet`s, so no schema migration is required unless they are deliberately introduced into the EF model later.
- Mapped Inventory and Finance identifier/FK contracts have targeted long-ID regression coverage.
- Mapped Payroll employee/user identity relations have targeted long-ID regression coverage. `Increment.ApprovedBy` remains snapshot-compatible legacy approval metadata rather than being treated as a proven FK; the earlier speculative width change was reverted after EF snapshot validation caught the mismatch.

## Roadmap

| Area | Status | Exit criteria |
| --- | --- | --- |
| Library operational workflow | Implemented; hardening/tests present | Keep build/tests green and close concrete workflow defects found during final review. |
| Transport operational workflow | Implemented; concurrency hardening/tests present | Keep build/tests green and close only concrete workflow defects found during final review. |
| Core education + self-service gaps | Substantially implemented | Audit remaining teacher/employee/student/guardian workflows and add only evidence-based fixes. |
| Cross-module integrity / long-ID normalization | In progress | No mapped operational FK/ID contract remains on a legacy incompatible width; migrations remain safe. |
| Security / tenant isolation / idempotency / concurrency | In progress | Mutations are authorized, tenant-scoped, replay-safe where required, and concurrency-sensitive writes are protected. |
| Automated tests / migration validation | In progress | Targeted regressions cover fixes; build, tests, EF migration/snapshot validation, and production publish validation are green. |
| Documentation cleanup | In progress | README/status accurately reflect implemented modules and operational requirements. |
| Production-readiness review | Pending | Final branch CI green; no known critical/high-severity correctness, isolation, migration, authorization, or publish blocker remains. |

## Closed hardening items

Transport assignment's duplicate-assignment and vehicle-capacity read/check/write flow is protected by a serializable transaction. Async transaction flow is enabled, successful writes complete the scope explicitly, and database/transaction serialization conflicts return HTTP 409 so callers can reload/retry. Targeted contract tests guard these invariants. This item is closed unless final review finds a concrete defect in the implementation.

Library stock mutation and issue close/return flows have explicit optimistic-concurrency model contracts. Operational Library/Transport read endpoints inherit authenticated controller boundaries, while privileged mutations retain role restrictions. Tenant query-filter regressions for the critical operational entities are covered by tests.

The deployable application now has a CI Release-publish gate. A green branch baseline therefore covers compilation, EF model/snapshot drift detection, automated regression tests, and generation of the production publish artifact; environment-specific deployment configuration and external infrastructure remain deployment-time concerns.

## Review discipline

Before changing a schema or relationship, inspect the mapped entity, EF configuration, current migration/snapshot, API/service consumers, and existing tests. Do not introduce a migration merely to normalize an orphan/unmapped legacy class. Prefer targeted regression tests for every security or integrity defect fixed. Do not promote an application retry/correlation key into a database uniqueness constraint without verifying existing production data and migration compatibility.

The branch is **not yet declared production-ready**. The remaining integrity and hardening audit, migration validation, documentation cleanup, and final review must complete before this status can be changed to complete.
