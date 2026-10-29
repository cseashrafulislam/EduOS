# Admission & Student V1 — code-side handoff

Branch: `Ashraful`. This page records implemented engineering behavior and
outstanding **code-owned** verification. It does not certify production or
alter the V1/V2/V3 release contract.

## Delivered

- Tenant-admin CSV/XLSX import: 100-row atomic windows, resumable jobs,
  durable per-source identities, cross-file preflight, private guardians,
  current enrollment, required subject registrations, role/tenant checks.
- Read-only integrity audit for committed import windows; report/export is
  bounded and does not expose global Person identifiers or protected learner
  details.
- Historical academic repair for **one existing 1–100-row import window**:
  `GET /api/student-imports/{reference}/academic-repair` previews missing
  mandatory registrations and a deterministic fingerprint.
  `POST /api/student-imports/{reference}/academic-repair` requires a
  CSRF-protected tenant-admin form, request ID and matching fingerprint.
- Repair rechecks import receipts, student/enrollment identity, required
  offerings, active curriculum, subject capacity and result history inside an
  atomic transaction **after deterministic AcademicBatch write locks**.
  A committed retry returns its original count; a competing/stale request
  returns a reloadable conflict. Unrelated students and other tenants cannot
  be updated.
- Only one unchanged, current active enrollment per imported student is
  eligible. Ambiguous/missing registrations, progressed/completed students,
  archived curriculum, insufficient offering capacity or calculated/published
  results require manual academic review. No global Person merge, grade,
  attendance, posted finance, historical enrollment or existing registration
  is rewritten. The result receipt and registration approval metadata are
  durable, with no new schema/migration.

## Engineering tests

- Unit: exact preview, repair, stale capacity, changed enrollment, cross-tenant
  privacy, non-admin denial, duplicate/replay and immutable integrity report.
- Real SQL Server: two competing repair requests for the same missing
  registration have exactly one winner, one 409, a single approved
  registration and exact request replay. SQL Server integration requires
  `EDUOS_SQL_AUTH_TEST_CONNECTION` on a disposable SQL Server instance.
- Existing SQL last-seat AcademicEnrollment/Import race, import capacity,
  admission conversion and readmission regressions remain enabled.
- The 40K single-run synthetic rehearsal passed on 2026-10-28; it is not proof
  of peak-hour multi-tenant throughput. Repeated restart/failure injection,
  multi-tenant load and real browser E2E remain release-verification work.

## Limits / post-publish acceptance

- There is intentionally **no blind bulk migration** of old records, no
  automatic Person/guardian merging, and no attempt to infer what a historical
  curriculum contained before edits. Unsafe rows remain in the bounded
  non-PII worklist until an authorized academic/identity decision is made.
- Institution-owned live UAT, actual admission import, production credentials,
  penetration review, deployment/rollback, restored-data rehearsal and
  operational sign-off are external release gates, **not unfinished source
  implementation** in the owner's code-only progress accounting.
