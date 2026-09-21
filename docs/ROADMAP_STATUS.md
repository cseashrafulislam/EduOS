# EduOS production-hardening roadmap status

This file tracks the production-readiness hardening work on `codex/phase-0-security-foundation`. It does **not** redefine planned product features in `README.md` as implemented; production-ready here means the currently implemented scope has no known critical/high-severity blocker found by this hardening review.

## Final verified baseline

- Branch: `codex/phase-0-security-foundation` (never `master`).
- Last code baseline before this documentation closeout: `0499ddb72fdc3e4de371a15eec3bc624252b314d`.
- GitHub Actions CI #837 passed on 2026-09-21 for that exact code baseline.
- CI validates Release build, EF pending-model/snapshot drift, the full automated test suite, and production publish of `EduOS.App`.
- Runtime tenant uploads are no longer tracked by source control and the upload tree is ignored to reduce accidental tenant-data leakage through repository history.
- Student promotion's roll uniqueness and section-capacity read/check/write invariants run inside a serializable transaction; serialization/database conflicts map to HTTP 409 and targeted contract tests guard the boundary.
- Library stock/issue lifecycle and Transport capacity/assignment concurrency protections have targeted regression coverage.
- Library/Transport and critical self-service operational surfaces have authorization/module-entitlement and tenant-isolation regression coverage.
- Realtime notifications are authenticated and tenant-isolated.
- Legacy identifier normalization was reviewed against actual EF mappings. Mapped operational FK/ID corrections are covered by regression/model validation; orphan/unmapped legacy classes do not receive speculative migrations.
- Sensitive operational/API responses reviewed during this hardening pass use no-store response caching where required.
- Canonical academic setup now has protected programme, level, subject, curriculum registration, batch and room write paths with tenant-owned reference validation, serializable retry handling, database natural-key constraints and targeted behavioral/model coverage.
- Canonical academic enrollment now links students to programme/level/batch/curriculum history, snapshots subject rules, enforces capacity/current-student/roll/idempotency invariants, supports learner/guardian elective approval, and filters owned timetables to approved subjects.
- Academic calendar management now supports campus-fallback weekend policy, year/term-bounded public and private events, privacy-safe working-day projection, idempotency, stale-write rejection and database-backed duplicate protection.

## Roadmap closeout

| Area | Status | Verification |
| --- | --- | --- |
| Library operational workflow | Complete for implemented scope | Authorization, tenant isolation, optimistic concurrency and mutation semantics covered. |
| Transport operational workflow | Complete for implemented scope | Serializable capacity/duplicate protection and targeted tests covered. |
| Core education + self-service hardening | Complete for implemented scope | Student/guardian/employee boundaries and evidence-based defects found in review are closed. |
| Cross-module integrity / long-ID normalization | Complete for mapped scope | EF mapping/snapshot validation is authoritative; no speculative migrations for unmapped legacy classes. |
| Canonical academic setup, enrollment, calendar + routine foundation | Complete for implemented scope | Programme-to-batch setup, curriculum/learner subject registration, ownership-scoped timetable reads, instructor assignment, collision-safe routine writes and calendar policy/event/working-day operations are tenant/module/role guarded and regression-covered; later academic workflows remain in README scope. |
| Security / tenant isolation / idempotency / concurrency | Complete for reviewed implemented scope | Critical mutation/read boundaries and discovered high-risk races are protected. |
| Automated tests / migration validation | Complete | CI gates Release build, EF model/snapshot validation, full tests and production publish. |
| Repository/runtime data hygiene | Complete | Runtime tenant upload content removed from tracking and ignored. |
| Documentation cleanup | Complete | This status distinguishes implemented-scope readiness from future product roadmap features. |
| Production-readiness review | Complete for implemented scope | No known critical/high-severity blocker remains from this hardening roadmap at the verified baseline. |

## Closed hardening items

Transport assignment's duplicate-assignment and vehicle-capacity read/check/write flow is protected by a serializable transaction. Async transaction flow is enabled, successful writes complete the scope explicitly, and database/transaction serialization conflicts return HTTP 409 so callers can reload/retry. Targeted contract tests guard these invariants.

Student promotion's target-roll uniqueness and section-capacity checks are also inside a serializable transaction. This prevents concurrent promotions from both observing the same free roll/seat and committing an invalid placement. Database, optimistic-concurrency, and transaction-abort conflicts are translated to HTTP 409 with targeted regression coverage.

Library stock mutation and issue close/return flows have explicit optimistic-concurrency model contracts. Operational Library/Transport read endpoints inherit authenticated controller boundaries, while privileged mutations retain role restrictions. Critical mutations are regression-guarded as POST-only and against accidental action-level anonymous authorization bypass. Tenant query-filter regressions for critical operational entities are covered by tests.

Student/guardian and employee self-service controllers have regression coverage for role boundaries, required module entitlements, accidental anonymous bypass, and expected GET/POST semantics. Employee leave application remains a protected POST mutation.

The deployable application has a CI Release-publish gate. A green branch baseline therefore covers compilation, EF model/snapshot drift detection, automated regression tests, and generation of the production publish artifact. Environment-specific deployment configuration, provider certification, external infrastructure, penetration testing, load testing, and future product features remain deployment/roadmap concerns rather than evidence that this hardening branch failed its implemented-scope gate.

## Scope boundary

`README.md` remains the product roadmap and intentionally lists future capabilities. Items marked planned/foundation there are **not** silently promoted to implemented by this document. A future feature becomes part of the production-readiness gate when it is implemented or explicitly added to a release scope.

Before any future schema/relationship change, inspect the mapped entity, EF configuration, current migration/snapshot, API/service consumers, and tests. Do not introduce migrations merely to normalize orphan/unmapped legacy classes. Prefer targeted regression tests for every security/integrity defect fixed, and do not turn retry/correlation keys into database uniqueness constraints without production-data compatibility evidence.

The production-hardening roadmap is closed for the currently implemented scope once CI is green on this documentation closeout commit. Any later code change reopens the gate and requires the same Release build, EF validation, full-test, and publish checks on the new exact SHA.
