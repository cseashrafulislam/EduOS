# EduOS delivery status

This document is the concise implementation status for the `codex/phase-0-security-foundation` delivery branch. It complements the product vision in the root README and should be updated when a workflow moves between planned, foundation, operational, or production-ready states.

## Current delivery state

### Operational workflows completed

- Institution signup/onboarding, plan or trial selection, payment progression, campus/academic/module/branding/general/gateway setup.
- Admission intake/review and approved applicant conversion into student, guardian and enrolment records.
- Annual student promotion/repeat and student transfer/completion foundations.
- Library operational workflow, including catalogue/copy circulation paths, issue/return/reservation/fine handling and tenant-safe operations.
- Transport operational workflow, including vehicles/routes/stops/student assignments, operational state and tenant-safe write paths.
- Core student, guardian, teacher and employee self-service slices delivered on the branch, with authorization and tenant context enforced at the application boundary.

### Security and data-integrity hardening completed

- Authentication-required fallback authorization policy; public routes must opt out explicitly with `AllowAnonymous`.
- Canonical tenant context resolution and fail-closed tenant query filtering.
- Stale role/tenant claim rejection and inactive-account session rejection.
- Privileged MFA flow for platform/tenant administrative roles.
- JWT validation pinned to the expected signing algorithm.
- Payment callbacks mutate state only after provider-side verification; anonymous fail/cancel callbacks do not create terminal payment state.
- Payment and subscription lifecycle side effects use optimistic-concurrency/idempotency gates.
- Collision-resistant invoice numbering.
- Runtime EF model and repository identifier boundaries are normalized to `long` for mapped domain identifiers; legacy shadow `*Id1` relationships are guarded against by tests.
- Data-reconciliation migrations fail fast on conflicting/orphaned legacy relationships instead of silently discarding identifiers.
- Critical async/null-safety compiler warnings (`CS4014`, `CS0649`, `CS8602`) are promoted to CI errors.

### Automated validation now enforced in CI

CI must pass all of the following before a branch change is considered complete:

1. `dotnet restore EduOS.slnx`
2. Release build of the full solution.
3. `dotnet ef migrations has-pending-model-changes` for `EduOSDbContext`.
4. Full automated test suite.

The committed `EduOSDbContextModelSnapshot` was regenerated from the current EF Core 10 runtime model on 2026-09-14. A regression contract also compares runtime entity/property/relationship metadata with the committed snapshot so future model drift is detected before merge.

## Migration policy

- Production deployments must run reviewed migrations as a separate release step; application startup does not implicitly mutate production schema.
- Identifier-widening migrations intentionally refuse unsafe `bigint -> int` rollback when data could be truncated.
- Legacy canonical/shadow identifier reconciliation checks for conflicts and orphans before dropping obsolete columns/FKs.
- Any entity-model change must leave `dotnet ef migrations has-pending-model-changes` clean in CI.

## Final code-readiness review

The agreed repository development sequence is complete through Library, Transport, core self-service, long-ID/FK normalization, security/tenant/idempotency/concurrency hardening, targeted regression tests, EF migration validation, and documentation cleanup. The final branch CI is required to remain green before merge.

The repository is **code-ready for a production-candidate deployment**, but production readiness cannot be certified from source control alone. The following release gates require a real deployment environment or external operational evidence:

- production secret/key custody and rotation verification;
- backup/restore and disaster-recovery rehearsal;
- load/performance testing against production-like infrastructure and data volume;
- SQL Server migration rehearsal against a production-like database copy;
- penetration/security testing and administrator recovery/break-glass rehearsal;
- production merchant/provider certification and external reconciliation/refund procedures.

These are release/operations gates, not unfinished repository implementation slices. Any future product roadmap capability listed as planned in the root README is outside this completed hardening/completion sequence unless separately promoted into scope.

## Known non-operational scaffolding

`EduOS.BackgroundJobs/Jobs/DailyJobs.cs` and `MonthlyJobs.cs` still contain placeholder examples and are not evidence of implemented scheduled attendance, backup, fee-reminder, monthly-fee-generation, or payroll automation. They must not be registered as production recurring jobs until each workflow receives tenant-safe execution context, idempotency, retry policy, observability and dedicated tests.

## Merge rule

Do not merge this branch into `master` until the final branch CI is green and the deployment/release owner accepts the external production gates above. `master` must remain unchanged during roadmap execution.
